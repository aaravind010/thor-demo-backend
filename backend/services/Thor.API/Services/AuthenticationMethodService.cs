using Microsoft.EntityFrameworkCore;
using Thor.Api.Exceptions;
using Thor.Api.Models;
using Thor.DataConnectionManager;
using Thor.DataLayer.Data;
using Thor.DataLayer.Models;
using Thor.DataLayer.Models.Tenants;
using Thor.DataLayer.Repositories;

namespace Thor.Api.Services;

/// <summary>
/// Backs <c>POST v{version}/authentication-methods</c>: validates the requested authentication type and
/// its fields against the Master metadata DB, writes each field's secret value to Secrets
/// Manager (see docs/architecture/ADR-CONNECTOR-CREDENTIAL-MANAGEMENT.md), then persists a named
/// <see cref="AuthenticationMethod"/> plus its <see cref="AuthenticationValue"/>s (secret ARN
/// only, no plaintext) in the caller's tenant database. Also backs
/// <c>GET v{version}/authentication-methods/types</c>: lists the <see cref="AuthenticationType"/>
/// reference rows from the Master metadata DB, optionally only those supported by one connector type,
/// and <c>GET v{version}/authentication-methods/types/{typeId}/fields</c>: lists the
/// <see cref="AuthenticationField"/>s a given authentication type requires.
/// </summary>
public sealed class AuthenticationMethodService(
    ITenantConnectionManager tenantConnectionManager,
    IMasterDbContextFactory masterDbContextFactory,
    MasterConnectionInfo masterConnectionInfo,
    IAuthenticationSecretWriter authenticationSecretWriter,
    ILogger<AuthenticationMethodService> logger)
{
    public async Task<AuthenticationMethodResponse> CreateAsync(
        Guid tenantId, string actorId, CreateAuthenticationMethodRequest request, CancellationToken cancellationToken)
    {
        using var masterDb = masterDbContextFactory.Create(masterConnectionInfo);
        var typeRepository = new AuthenticationTypeRepository(masterDb);

        var type = await typeRepository.GetByIdAsync(request.TypeId, cancellationToken)
            ?? throw new AuthenticationTypeNotFoundException(request.TypeId);

        var fieldRepository = new AuthenticationFieldRepository(masterDb);
        var fields = await fieldRepository.GetByTypeIdAsync(type.Id, cancellationToken);

        ValidateFields(fields, request.Values);

        using var tenantDb = await tenantConnectionManager.GetTenantDbContextAsync(tenantId, cancellationToken);
        var methodRepository = new AuthenticationMethodRepository(tenantDb);

        var secretName = AuthenticationSecretNaming.SecretName(tenantId, type.Id);

        await using var transaction = await tenantDb.Database.BeginTransactionAsync(cancellationToken);

        // Postgres advisory lock, scoped to this tenant+authentication-type secret, held for
        // the lifetime of this transaction and released automatically on commit or rollback.
        // Serializes the Secrets Manager read-modify-write below across every caller — including
        // a different Thor.Api/ECS task — closing the race an in-process-only lock could not
        // (see docs/architecture/ADR-CONNECTOR-CREDENTIAL-MANAGEMENT.md).
        await tenantDb.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_xact_lock(hashtext({secretName}))", cancellationToken);

        logger.LogDebug("Acquired advisory lock for secret {SecretName}", secretName);

        var now = DateTimeOffset.UtcNow;
        var method = new AuthenticationMethod
        {
            Id = Guid.NewGuid(),
            TypeId = request.TypeId,
            Name = request.Name,
            Description = request.Description,
        };

        var valuesByRowId = new Dictionary<Guid, string>();

        foreach (var value in request.Values)
        {
            var valueId = Guid.NewGuid();
            valuesByRowId[valueId] = value.Value;

            method.AuthenticationValues.Add(new AuthenticationValue
            {
                Id = valueId,
                MethodId = method.Id,
                FieldId = value.FieldId,
                CreatedAt = now,
                UpdatedAt = now,
                CreatedBy = actorId,
                UpdatedBy = actorId,
            });
        }

        // One request has exactly one authentication type, so every field value here belongs
        // to the same tenant+type secret — write them all in a single get-then-put rather than
        // one round trip per field (see IAuthenticationSecretWriter).
        var secretArn = await authenticationSecretWriter.StoreValuesAsync(
            tenantId, type.Id, valuesByRowId, cancellationToken);

        logger.LogInformation("Stored authentication secret at {SecretArn}", secretArn);

        foreach (var authenticationValue in method.AuthenticationValues)
        {
            authenticationValue.SecretArn = secretArn;
        }

        await methodRepository.AddAsync(method, cancellationToken);
        await tenantDb.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        logger.LogInformation(
            "Persisted authentication method {AuthenticationMethodId} with {ValueCount} value(s)",
            method.Id, method.AuthenticationValues.Count);

        return new AuthenticationMethodResponse(
            method.Id,
            method.TypeId,
            method.Name,
            method.Description,
            method.AuthenticationValues.Select(v => v.Id).ToList());
    }

    public async Task<IReadOnlyList<AuthenticationTypeResponse>> ListTypesAsync(short? connectorType, CancellationToken cancellationToken)
    {
        using var masterDb = masterDbContextFactory.Create(masterConnectionInfo);
        var typeRepository = new AuthenticationTypeRepository(masterDb);

        var types = connectorType is { } filter
            ? await typeRepository.GetByConnectorTypeAsync(filter, cancellationToken)
            : await typeRepository.GetAllAsync(cancellationToken);

        // Authentication types are deployment seed data, so an empty unfiltered table means the
        // Master DB seed hasn't run (or failed) rather than a normal state. A filtered result can
        // legitimately be empty (the connector has no mapped types yet).
        if (types.Count == 0 && connectorType is null)
        {
            logger.LogWarning("No authentication types found in the Master DB; deployment seed may be missing");
        }
        else
        {
            logger.LogDebug("Listed {AuthenticationTypeCount} authentication type(s) for connector type {ConnectorType}", types.Count, connectorType);
        }

        return types
            .OrderBy(t => t.Name, StringComparer.Ordinal)
            .Select(t => new AuthenticationTypeResponse(t.Id, t.Name))
            .ToList();
    }

    public async Task<IReadOnlyList<AuthenticationFieldResponse>> ListFieldsAsync(Guid typeId, CancellationToken cancellationToken)
    {
        using var masterDb = masterDbContextFactory.Create(masterConnectionInfo);
        var typeRepository = new AuthenticationTypeRepository(masterDb);

        // Distinguish an unknown type (404) from a known type that defines no fields (empty list).
        _ = await typeRepository.GetByIdAsync(typeId, cancellationToken)
            ?? throw new AuthenticationTypeNotFoundException(typeId);

        var fieldRepository = new AuthenticationFieldRepository(masterDb);
        var fields = await fieldRepository.GetByTypeIdAsync(typeId, cancellationToken);

        logger.LogDebug("Listed {AuthenticationFieldCount} authentication field(s) for authentication type {AuthenticationTypeId}", fields.Count, typeId);

        return fields
            .OrderBy(f => f.Name, StringComparer.Ordinal)
            .Select(f => new AuthenticationFieldResponse(f.Id, f.Name, f.DisplayName, f.InputType, f.Description))
            .ToList();
    }

    private static void ValidateFields(IReadOnlyList<AuthenticationField> fields, IReadOnlyList<AuthenticationValueRequest> values)
    {
        var requiredFieldIds = fields.Select(f => f.Id).ToHashSet();
        var submittedFieldIds = values.Select(v => v.FieldId).ToHashSet();

        var missing = requiredFieldIds.Except(submittedFieldIds).ToList();
        var unknown = submittedFieldIds.Except(requiredFieldIds).ToList();

        if (missing.Count > 0 || unknown.Count > 0)
        {
            throw new InvalidAuthenticationFieldsException(missing, unknown);
        }
    }
}

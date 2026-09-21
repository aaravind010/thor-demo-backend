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
/// only, no plaintext) in the caller's tenant database. The Secrets Manager write and the DB
/// save happen inside one tenant-DB transaction guarded by a Postgres advisory lock scoped to
/// the tenant + authentication type, so concurrent requests for the same secret — even from a
/// different Thor.Api/ECS task — serialize instead of racing.
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

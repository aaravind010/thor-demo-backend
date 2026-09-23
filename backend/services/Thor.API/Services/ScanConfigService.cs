using Thor.Api.Exceptions;
using Thor.Api.Models;
using Thor.DataConnectionManager;
using Thor.DataLayer.Data;
using Thor.DataLayer.Models;
using Thor.DataLayer.Models.Tenants;
using Thor.DataLayer.Repositories;

namespace Thor.Api.Services;

/// <summary>
/// Backs <c>POST /scan-config</c>: validates that the requested sources share one connector
/// type, that the authentication method exists and matches that connector type, and that the
/// submitted connector config values match that connector's config fields in the Master
/// metadata DB, then persists a
/// <see cref="ScanConfig"/> together with its <see cref="ScanSourceMapping"/> and
/// <see cref="ScanConnectorConfigValue"/> rows in the caller's tenant database as one
/// transaction — no partial data is left behind if any step fails.
/// </summary>
public sealed class ScanConfigService(
    ITenantConnectionManager tenantConnectionManager,
    IMasterDbContextFactory masterDbContextFactory,
    MasterConnectionInfo masterConnectionInfo)
{
    public async Task<ScanConfigResponse> CreateAsync(
        Guid tenantId, string actorId, CreateScanConfigRequest request, CancellationToken cancellationToken)
    {
        using var tenantDb = await tenantConnectionManager.GetTenantDbContextAsync(tenantId, cancellationToken);
        using var masterDb = masterDbContextFactory.Create(masterConnectionInfo);

        var connectorTypeRepository = new ConnectorTypeRepository(masterDb);

        var sourceRepository = new SourceRepository(tenantDb);
        var sources = await sourceRepository.GetByIdsAsync(request.SourceIds.ToHashSet(), cancellationToken);

        var foundSourceIds = sources.Select(s => s.Id).ToHashSet();
        var missingSourceIds = request.SourceIds.Where(id => !foundSourceIds.Contains(id)).ToList();

        if (missingSourceIds.Count > 0)
        {
            throw new SourceNotFoundException(missingSourceIds);
        }

        var connectorTypes = sources.Select(s => s.ConnectorType).Distinct().ToList();

        if (connectorTypes.Count > 1)
        {
            var names = await ResolveConnectorTypeNamesAsync(connectorTypeRepository, connectorTypes, cancellationToken);
            throw new MixedSourceConnectorTypesException(connectorTypes.Select(t => names[t]).ToList());
        }

        var connectorType = connectorTypes[0];

        var authMethodRepository = new AuthenticationMethodRepository(tenantDb);
        var authMethod = await authMethodRepository.GetByIdAsync(request.AuthMethodId, cancellationToken)
            ?? throw new AuthenticationMethodNotFoundException(request.AuthMethodId);

        var authTypeRepository = new AuthenticationTypeRepository(masterDb);
        var authType = await authTypeRepository.GetByIdAsync(authMethod.TypeId, cancellationToken)
            ?? throw new AuthenticationTypeNotFoundException(authMethod.TypeId);

        if (authType.ConnectorTypeId != connectorType)
        {
            var ids = new[] { authType.ConnectorTypeId, connectorType };
            var names = await ResolveConnectorTypeNamesAsync(connectorTypeRepository, ids, cancellationToken);
            throw new AuthMethodConnectorTypeMismatchException(names[authType.ConnectorTypeId], names[connectorType]);
        }

        var configFieldRepository = new ConnectorConfigFieldRepository(masterDb);
        var configFields = await configFieldRepository.GetByConnectorTypeAsync(connectorType, cancellationToken);

        ValidateConfigValues(configFields, request.ConfigValues);

        var now = DateTimeOffset.UtcNow;
        var scanConfig = new ScanConfig
        {
            Id = Guid.NewGuid(),
            Name = request.Name,
            Description = request.Description,
            AuthMethodId = authMethod.Id,
            CreatedAt = now,
            UpdatedAt = now,
            CreatedBy = actorId,
            UpdatedBy = actorId,
        };

        foreach (var sourceId in request.SourceIds)
        {
            scanConfig.SourceMappings.Add(new ScanSourceMapping
            {
                Id = Guid.NewGuid(),
                ScanConfigId = scanConfig.Id,
                SourceId = sourceId,
            });
        }

        foreach (var value in request.ConfigValues)
        {
            scanConfig.ConnectorConfigValues.Add(new ScanConnectorConfigValue
            {
                Id = Guid.NewGuid(),
                ScanConfigId = scanConfig.Id,
                ConnectorType = connectorType,
                ConfigId = value.ConfigId,
                Value = value.Value,
                CreatedAt = now,
                UpdatedAt = now,
                CreatedBy = actorId,
                UpdatedBy = actorId,
            });
        }

        var scanConfigRepository = new ScanConfigRepository(tenantDb);

        await using var transaction = await tenantDb.Database.BeginTransactionAsync(cancellationToken);

        await scanConfigRepository.AddAsync(scanConfig, cancellationToken);
        await tenantDb.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return new ScanConfigResponse(scanConfig.Id);
    }

    private static async Task<Dictionary<short, string>> ResolveConnectorTypeNamesAsync(
        ConnectorTypeRepository connectorTypeRepository, IReadOnlyList<short> ids, CancellationToken cancellationToken)
    {
        var connectorTypes = await connectorTypeRepository.GetByIdsAsync(ids, cancellationToken);
        var names = connectorTypes.ToDictionary(c => c.Id, c => c.Name);

        foreach (var id in ids)
        {
            names.TryAdd(id, id.ToString());
        }

        return names;
    }

    private static void ValidateConfigValues(IReadOnlyList<ConnectorConfigField> fields, IReadOnlyList<ScanConnectorConfigValueRequest> values)
    {
        var allFieldIds = fields.Select(f => f.Id).ToHashSet();
        var requiredFieldIds = fields.Where(f => f.Required).Select(f => f.Id).ToHashSet();
        var submittedFieldIds = values.Select(v => v.ConfigId).ToHashSet();

        var missing = requiredFieldIds.Except(submittedFieldIds).ToList();
        var unknown = submittedFieldIds.Except(allFieldIds).ToList();

        if (missing.Count > 0 || unknown.Count > 0)
        {
            throw new InvalidScanConnectorConfigValuesException(missing, unknown);
        }
    }
}

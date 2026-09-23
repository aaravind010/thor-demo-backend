using Thor.Api.Exceptions;
using Thor.Api.Models;
using Thor.DataConnectionManager;
using Thor.DataLayer.Data;
using Thor.DataLayer.Models.Tenants;
using Thor.DataLayer.Repositories;

namespace Thor.Api.Services;

/// <summary>
/// Backs <c>POST /source</c> and <c>GET /source</c>: validates the requested connector type
/// against the Master metadata DB, then creates or lists <see cref="Source"/> rows in the
/// caller's tenant database.
/// </summary>
public sealed class SourceService(
    ITenantConnectionManager tenantConnectionManager,
    IMasterDbContextFactory masterDbContextFactory,
    MasterConnectionInfo masterConnectionInfo)
{
    public async Task<SourceResponse> CreateAsync(
        Guid tenantId, CreateSourceRequest request, CancellationToken cancellationToken)
    {
        using var masterDb = masterDbContextFactory.Create(masterConnectionInfo);
        var connectorTypeRepository = new ConnectorTypeRepository(masterDb);

        _ = await connectorTypeRepository.GetByIdAsync(request.ConnectorType, cancellationToken)
            ?? throw new ConnectorTypeNotFoundException(request.ConnectorType);

        using var tenantDb = await tenantConnectionManager.GetTenantDbContextAsync(tenantId, cancellationToken);
        var sourceRepository = new SourceRepository(tenantDb);

        var now = DateTimeOffset.UtcNow;
        var source = new Source
        {
            Id = Guid.NewGuid(),
            ConnectorType = request.ConnectorType,
            Name = request.Name,
            Config = request.Config,
            IsActive = true,
            CreatedAt = now,
            UpdatedAt = now,
        };

        await using var transaction = await tenantDb.Database.BeginTransactionAsync(cancellationToken);
        await sourceRepository.AddAsync(source, cancellationToken);
        await tenantDb.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return ToResponse(source);
    }

    public async Task<IReadOnlyList<SourceResponse>> ListAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        using var tenantDb = await tenantConnectionManager.GetTenantDbContextAsync(tenantId, cancellationToken);
        var sourceRepository = new SourceRepository(tenantDb);

        var sources = await sourceRepository.GetAllAsync(cancellationToken);
        return sources.Select(ToResponse).ToList();
    }

    private static SourceResponse ToResponse(Source source) => new(
        source.Id, source.ConnectorType, source.Name, source.Config, source.IsActive, source.CreatedAt, source.UpdatedAt);
}

using Thor.Api.Exceptions;
using Thor.Api.Models;
using Thor.DataConnectionManager;
using Thor.DataLayer.Models.Tenants;
using Thor.DataLayer.Repositories;

namespace Thor.Api.Services;

/// <summary>
/// Backs <c>POST /scan</c>: validates that the requested <see cref="ScanConfig"/> exists, then
/// persists a new <see cref="Scan"/> together with one <see cref="ScanTask"/> per source mapped
/// to that config, in the caller's tenant database, as one transaction.
/// </summary>
public sealed class ScanService(ITenantConnectionManager tenantConnectionManager)
{
    public async Task<ScanResponse> CreateAsync(
        Guid tenantId, CreateScanRequest request, CancellationToken cancellationToken)
    {
        using var tenantDb = await tenantConnectionManager.GetTenantDbContextAsync(tenantId, cancellationToken);

        var scanConfigRepository = new ScanConfigRepository(tenantDb);
        var scanConfig = await scanConfigRepository.GetByIdWithSourcesAsync(request.ScanConfigId, cancellationToken)
            ?? throw new ScanConfigNotFoundException(request.ScanConfigId);

        var sources = scanConfig.SourceMappings
            .Where(m => m.Source is not null)
            .Select(m => m.Source!)
            .ToList();

        var scanRepository = new ScanRepository(tenantDb);

        var scan = new Scan
        {
            Id = Guid.NewGuid(),
            ScanConfigId = scanConfig.Id,
            ScanType = request.ScanType,
            Status = ScanStatus.Pending,
            TotalTasks = sources.Count,
            CompletedTasks = 0,
        };

        var tasks = new List<ScanTask>();
        var taskResponses = new List<ScanTaskResponse>();

        foreach (var source in sources)
        {
            var task = new ScanTask
            {
                Id = Guid.NewGuid(),
                ScanId = scan.Id,
                SourceId = source.Id,
                Status = ScanTaskStatus.Pending,
            };

            tasks.Add(task);
            taskResponses.Add(new ScanTaskResponse(task.Id, source.ConnectorType, source.Id, task.Status));
        }

        var scanTaskRepository = new ScanTaskRepository(tenantDb);

        await using var transaction = await tenantDb.Database.BeginTransactionAsync(cancellationToken);

        await scanRepository.AddAsync(scan, cancellationToken);
        foreach (var task in tasks)
        {
            await scanTaskRepository.AddAsync(task, cancellationToken);
        }
        await tenantDb.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return new ScanResponse(scan.Id, scan.ScanConfigId, scan.Status, taskResponses);
    }
}

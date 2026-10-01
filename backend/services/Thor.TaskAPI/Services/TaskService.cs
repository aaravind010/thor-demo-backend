using System.Text.Json;
using Thor.DataConnectionManager;
using Thor.DataLayer.Data;
using Thor.DataLayer.Models;
using Thor.DataLayer.Models.Tenants;
using Thor.DataLayer.Repositories;
using Thor.TaskApi.Models;

namespace Thor.TaskApi.Services;

/// <summary>Task-related operations for connectors, backed by the caller's tenant database.</summary>
public sealed class TaskService(
    ITenantConnectionManager tenantConnectionManager,
    ScanTaskReclaimOptions reclaimOptions,
    IMasterDbContextFactory masterDbContextFactory,
    MasterConnectionInfo masterConnectionInfo,
    IAuthenticationSecretReader authenticationSecretReader)
{
    private const int MaxTasksPerRequest = 50;

    /// <summary>
    /// Backs <c>GET /task</c>: atomically claims the oldest pending <see cref="Thor.DataLayer.Models.Tenants.ScanTask"/>
    /// rows (FIFO) in the caller's tenant database, so each task is only ever handed to one connector.
    /// </summary>
    public async Task<TaskListResponse> ClaimTasksAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        using var tenantDb = await tenantConnectionManager.GetTenantDbContextAsync(tenantId, cancellationToken);

        var scanTaskRepository = new ScanTaskRepository(tenantDb);

        var claimed = await scanTaskRepository.ClaimPendingTasksAsync(
            MaxTasksPerRequest, reclaimOptions.StallTimeout, reclaimOptions.MaxRetries, cancellationToken);

        var tasks = claimed
            .Select(t => new ConnectorTaskResponse(
                t.ScanId,
                t.Scan.ScanConfigId,
                t.Id,
                t.Source.ConnectorType,
                t.Scan.ScanConfig.AuthMethodId,
                t.SourceId))
            .ToList();

        return new TaskListResponse(tasks);
    }

    /// <summary>
    /// Backs <c>POST /task/{taskId}/heartbeat</c>: confirms to the tenant database that a
    /// connector is still actively working <paramref name="taskId"/>, so the stall-reclaim sweep
    /// in <see cref="ClaimTasksAsync"/> leaves it alone.
    /// </summary>
    public async Task<ScanTaskHeartbeatResult> HeartbeatAsync(Guid tenantId, Guid taskId, CancellationToken cancellationToken)
    {
        using var tenantDb = await tenantConnectionManager.GetTenantDbContextAsync(tenantId, cancellationToken);

        var scanTaskRepository = new ScanTaskRepository(tenantDb);

        return await scanTaskRepository.RefreshHeartbeatAsync(taskId, cancellationToken);
    }

    /// <summary>
    /// Backs <c>PUT /task/{taskId}/status</c>: records the outcome a connector reports for
    /// <paramref name="taskId"/> in the tenant database, then — for a terminal outcome — rolls it
    /// up into the parent <see cref="Scan"/>: a single failed task fails the whole scan; a scan
    /// completes once every one of its tasks has completed. Reporting <see cref="ScanTaskStatus.Pending"/>
    /// just releases the task back to the queue and has no effect on the scan.
    /// </summary>
    public async Task<ScanTaskStatusUpdateResult> UpdateStatusAsync(Guid tenantId, Guid taskId, string status, CancellationToken cancellationToken)
    {
        using var tenantDb = await tenantConnectionManager.GetTenantDbContextAsync(tenantId, cancellationToken);

        var scanTaskRepository = new ScanTaskRepository(tenantDb);

        var (result, scanId) = await scanTaskRepository.UpdateStatusAsync(taskId, status, cancellationToken);

        if (result == ScanTaskStatusUpdateResult.Updated)
        {
            var scanRepository = new ScanRepository(tenantDb);

            if (status == Thor.DataLayer.Models.Tenants.ScanTaskStatus.Failed)
            {
                await scanRepository.MarkFailedAsync(scanId, cancellationToken);
            }
            else if (status == Thor.DataLayer.Models.Tenants.ScanTaskStatus.Completed)
            {
                await scanRepository.IncrementCompletedTasksAsync(scanId, cancellationToken);
                await scanRepository.CompleteIfAllTasksCompletedAsync(scanId, cancellationToken);
            }
        }

        return result;
    }

    /// <summary>
    /// Backs <c>GET /tasks/{taskId}/settings</c>: looks up <paramref name="taskId"/> in the
    /// caller's tenant database and assembles the connector-specific config and decrypted
    /// authentication field values its <see cref="ScanConfig"/> was created with, so the
    /// connector can authenticate against its target system and read its own config. Returns
    /// <see langword="null"/> if no such task exists in the tenant database.
    /// </summary>
    public async Task<TaskSettingsResponse?> GetSettingsAsync(Guid tenantId, Guid taskId, CancellationToken cancellationToken)
    {
        using var tenantDb = await tenantConnectionManager.GetTenantDbContextAsync(tenantId, cancellationToken);
        var scanTaskRepository = new ScanTaskRepository(tenantDb);

        var task = await scanTaskRepository.GetByIdWithSettingsAsync(taskId, cancellationToken);
        if (task is null)
        {
            return null;
        }

        var scanConfig = task.Scan.ScanConfig;

        using var masterDb = masterDbContextFactory.Create(masterConnectionInfo);

        var settings = await BuildSettingsJsonAsync(masterDb, task.Source.ConnectorType, scanConfig.ConnectorConfigValues, cancellationToken);
        var authMethod = await BuildAuthMethodJsonAsync(masterDb, scanConfig.AuthenticationMethod, cancellationToken);

        return new TaskSettingsResponse(task.Id, task.ScanId, scanConfig.Id, settings, authMethod);
    }

    /// <summary>
    /// Backs <c>POST /tasks/{taskId}/progress</c>: appends a progress update a connector reports
    /// for <paramref name="taskId"/> as a new row in the tenant database. Returns
    /// <see langword="null"/> if no such task exists in the tenant database.
    /// </summary>
    public async Task<Guid?> AddProgressAsync(
        Guid tenantId, Guid taskId, long timeElapsed, int percentageComplete, string attributesJson, CancellationToken cancellationToken)
    {
        using var tenantDb = await tenantConnectionManager.GetTenantDbContextAsync(tenantId, cancellationToken);
        var taskProgressRepository = new TaskProgressRepository(tenantDb);

        var progress = new TaskProgress
        {
            Id = Guid.NewGuid(),
            TaskId = taskId,
            PercentageCompleted = percentageComplete,
            Attributes = attributesJson,
            TimeElapsed = timeElapsed,
            CreatedAt = DateTimeOffset.UtcNow,
        };

        var added = await taskProgressRepository.AddIfTaskExistsAsync(progress, cancellationToken);
        return added ? progress.Id : null;
    }

    // Connector config values are stored per-field as opaque strings keyed by a Master-DB field
    // id (see ScanConnectorConfigValue); resolving each id to its field name here makes the
    // JSON handed to the connector self-describing instead of a bag of guids, using the field
    // name exactly as stored in the Master DB (see Scripts/Seed/04_connector_config_fields.sql).
    private static async Task<string> BuildSettingsJsonAsync(
        MasterDbContext masterDb, short connectorType, IEnumerable<ScanConnectorConfigValue> configValues, CancellationToken cancellationToken)
    {
        var configFieldRepository = new ConnectorConfigFieldRepository(masterDb);
        var fields = await configFieldRepository.GetByConnectorTypeAsync(connectorType, cancellationToken);
        var fieldNamesById = fields.ToDictionary(f => f.Id, f => f.FieldName);

        var settings = configValues.ToDictionary(
            v => fieldNamesById.TryGetValue(v.ConfigId, out var name) ? name : v.ConfigId.ToString(),
            v => v.Value);

        return JsonSerializer.Serialize(settings);
    }

    // Every AuthenticationValue for one AuthenticationMethod shares the same tenant+type secret
    // (see docs/architecture/ADR-CONNECTOR-CREDENTIAL-MANAGEMENT.md) — credentials are grouped
    // that way specifically to keep the number of secrets (and so Secrets Manager calls) down, so
    // rather than reading once per AuthenticationValue, every value is grouped by its SecretArn
    // first and each distinct secret is read exactly once (concurrently), then resolved back to
    // its owning field.
    private async Task<string> BuildAuthMethodJsonAsync(
        MasterDbContext masterDb, AuthenticationMethod authenticationMethod, CancellationToken cancellationToken)
    {
        var authFieldRepository = new AuthenticationFieldRepository(masterDb);
        var fields = await authFieldRepository.GetByTypeIdAsync(authenticationMethod.TypeId, cancellationToken);
        var fieldNamesById = fields.ToDictionary(f => f.Id, f => f.Name);

        var distinctArns = authenticationMethod.AuthenticationValues.Select(v => v.SecretArn).Distinct().ToList();
        var fetchedSecrets = await Task.WhenAll(distinctArns.Select(async arn =>
            (Arn: arn, Values: await authenticationSecretReader.GetValuesAsync(arn, cancellationToken))));
        var secretsByArn = fetchedSecrets.ToDictionary(s => s.Arn, s => s.Values);

        var authMethodValues = new Dictionary<string, string>();

        foreach (var value in authenticationMethod.AuthenticationValues)
        {
            var fieldName = fieldNamesById.TryGetValue(value.FieldId, out var name) ? name : value.FieldId.ToString();
            authMethodValues[fieldName] = secretsByArn[value.SecretArn][value.Id];
        }

        return JsonSerializer.Serialize(authMethodValues);
    }
}

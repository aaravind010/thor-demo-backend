namespace Thor.TaskApi.Services;

/// <summary>
/// Controls when a stalled <see cref="Thor.DataLayer.Models.Tenants.ScanTask"/> is reclaimed
/// back to Pending, and when it's given up on entirely (Dead), in <see cref="TaskService.ClaimTasksAsync"/>.
/// No fallback defaults — both values must be set via environment variables (see Program.cs).
/// </summary>
public sealed record ScanTaskReclaimOptions(TimeSpan StallTimeout, int MaxRetries);

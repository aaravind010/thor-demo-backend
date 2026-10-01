using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Thor.DataLayer.Models.Tenants;

/// <summary>
/// Well-known values for <see cref="ScanTask.Status"/>.
/// </summary>
public static class ScanTaskStatus
{
    public const string Pending = "Pending";
    public const string InProgress = "InProgress";
    public const string Completed = "Completed";
    public const string Failed = "Failed";

    /// <summary>
    /// Stalled in <see cref="InProgress"/> and reclaimed too many times (see
    /// <see cref="ScanTaskRepository.ClaimPendingTasksAsync"/>) — given up on, distinct from
    /// <see cref="Failed"/> (an explicit failure reported by the connector).
    /// </summary>
    public const string Dead = "Dead";

    /// <summary>
    /// Statuses a connector may report via <c>PUT /task/{taskId}/status</c>: the terminal
    /// outcomes (<see cref="Completed"/>, <see cref="Failed"/>), plus <see cref="Pending"/> so a
    /// connector can voluntarily release a claimed task back to the queue (e.g. a transient
    /// failure it wants retried, distinct from <see cref="Failed"/>). <see cref="InProgress"/>
    /// and <see cref="Dead"/> stay internal states owned by the claim/reclaim sweep (see
    /// <see cref="ScanTaskRepository.ClaimPendingTasksAsync"/>), not settable directly.
    /// </summary>
    public static readonly IReadOnlySet<string> ConnectorSettableStatuses = new HashSet<string> { Pending, Completed, Failed };
}

/// <summary>
/// A unit of work executed within a <see cref="Scan"/> (see ADR §12). Maps to
/// the "scan_task" table.
/// </summary>
[Table("scan_task")]
public sealed class ScanTask
{
    [Key]
    [Column("id")]
    public Guid Id { get; set; }

    [Column("scan_id")]
    [ForeignKey(nameof(Scan))]
    public Guid ScanId { get; set; }

    [Column("source_id")]
    [ForeignKey(nameof(Source))]
    public Guid SourceId { get; set; }

    [Column("status")]
    public string Status { get; set; } = null!;

    [Column("started_at")]
    public DateTimeOffset? StartedAt { get; set; }

    /// <summary>
    /// Last time a connector confirmed (via <c>POST /task/{id}/heartbeat</c>) that it's still
    /// working this task. Set alongside <see cref="StartedAt"/> on every claim, then refreshed
    /// only by the heartbeat call. Drives stall detection instead of <see cref="StartedAt"/> —
    /// see <see cref="ScanTaskRepository.ClaimPendingTasksAsync"/>.
    /// </summary>
    [Column("last_heartbeat_at")]
    public DateTimeOffset? LastHeartbeatAt { get; set; }

    [Column("completed_at")]
    public DateTimeOffset? CompletedAt { get; set; }

    /// <summary>
    /// When this task was created. Used to order pending tasks FIFO when connectors pull
    /// work via <c>GET /task</c> — see <see cref="ScanTaskRepository.ClaimPendingTasksAsync"/>.
    /// </summary>
    [Column("created_at")]
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>
    /// How many times this task has been reclaimed from a stalled <see cref="ScanTaskStatus.InProgress"/>
    /// back to <see cref="ScanTaskStatus.Pending"/>. Once this would exceed the configured max,
    /// the task is moved to <see cref="ScanTaskStatus.Dead"/> instead — see
    /// <see cref="ScanTaskRepository.ClaimPendingTasksAsync"/>.
    /// </summary>
    [Column("retry_count")]
    public int RetryCount { get; set; }

    public Scan Scan { get; set; } = null!;

    public Source Source { get; set; } = null!;
}

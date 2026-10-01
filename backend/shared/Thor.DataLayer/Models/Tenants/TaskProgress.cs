using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Thor.DataLayer.Models.Tenants;

/// <summary>
/// One progress update a connector reported for a <see cref="Tenants.ScanTask"/> (see
/// ADR §12). Append-only log: every <c>POST /tasks/{taskId}/progress</c> call inserts a new
/// row rather than updating an existing one, so the full progress history for a task is
/// preserved. Maps to the "task_progress" table.
/// </summary>
[Table("task_progress")]
public sealed class TaskProgress
{
    [Key]
    [Column("id")]
    public Guid Id { get; set; }

    [Column("task_id")]
    [ForeignKey(nameof(ScanTask))]
    public Guid TaskId { get; set; }

    [Column("percentage_completed")]
    public int PercentageCompleted { get; set; }

    /// <summary>
    /// Connector-reported item counts (e.g. <c>{"users": 20, "groups": 10}</c>), stored as-is.
    /// Arbitrary JSON object with numeric field values — not a fixed schema, so it's kept as raw
    /// JSON text (mapped to a "jsonb" column, see <c>TaskProgressConfiguration</c>) rather than a
    /// typed model.
    /// </summary>
    [Column("attributes")]
    public string Attributes { get; set; } = null!;

    /// <summary>Connector-reported elapsed processing time, in milliseconds.</summary>
    [Column("time_elapsed")]
    public long TimeElapsed { get; set; }

    [Column("created_at")]
    public DateTimeOffset CreatedAt { get; set; }

    public ScanTask ScanTask { get; set; } = null!;
}

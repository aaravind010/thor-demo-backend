using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace Thor.DataLayer.Models.Tenants;

/// <summary>
/// Tracks one Neptune S3 bulk-load job, carrying its <see cref="LoadId"/> from the container
/// invocation that started it (<c>graph-load-start</c>) to the one that polls it
/// (<c>graph-load-poll</c>) — two separate container invocations share no in-memory state, and
/// Step Functions' ECS task integration doesn't shuttle arbitrary app data between them.
/// </summary>
[Table("graph_bulk_load_job")]
[Index(nameof(ScanId), nameof(Status))]
public sealed class GraphBulkLoadJob
{
    [Key]
    [Column("id")]
    public Guid Id { get; set; }

    [Column("scan_id")]
    public Guid ScanId { get; set; }

    [Column("load_id")]
    public string LoadId { get; set; } = null!;

    [Column("s3_uri")]
    public string S3Uri { get; set; } = null!;

    /// <summary>"started" | "completed" | "failed".</summary>
    [Column("status")]
    public string Status { get; set; } = null!;

    [Column("started_at")]
    public DateTimeOffset StartedAt { get; set; }

    [Column("completed_at")]
    public DateTimeOffset? CompletedAt { get; set; }

    [Column("error_summary")]
    public string? ErrorSummary { get; set; }
}

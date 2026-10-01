using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Thor.DataLayer.Models.Tenants;

/// <summary>
/// Well-known values for <see cref="ScanFile.Status"/>. Owned entirely by ingestion — the
/// connector never writes a file status.
/// </summary>
public static class ScanFileStatus
{
    /// <summary>Recorded when a manifest is created; not yet processed.</summary>
    public const string Received = "Received";

    public const string Ingested = "Ingested";

    public const string Failed = "Failed";
}

/// <summary>
/// One uploaded file within a scan, and how ingestion fared with it. The unit that makes
/// "3 of 5 sources ingested" answerable: a source's files scatter across manifests, so status is
/// aggregated per <see cref="ScanTask"/> — which is keyed (scan, source) — and never per manifest.
///
/// <para>
/// <see cref="ScanTaskId"/> comes from the object key, which Thor.TaskApi builds as
/// <c>tenants/{tenantId}/uploads/{scanId}/{sourceId}/data_{taskId}_{sourceId}_{uuidv7}</c>. Attribute
/// on the <c>taskId</c> segment, which UploadService took from a ScanTask it looked up, rather than
/// the <c>{sourceId}</c> path segment, which is caller-supplied and never checked against that task.
/// </para>
///
/// <para>
/// The unique index on (scan_id, file_location) is load-bearing: Step Functions retries each Map item
/// up to four times and SQS delivery is at-least-once, so every write is an upsert keyed on the file.
/// Because status is a column keyed by file rather than a counter, a first attempt writing Failed and
/// a later one writing Ingested converges on the right answer. A processed-files counter cannot.
/// </para>
/// </summary>
[Table("scan_file")]
public sealed class ScanFile
{
    [Key]
    [Column("id")]
    public Guid Id { get; set; }

    [Column("scan_id")]
    [ForeignKey(nameof(Scan))]
    public Guid ScanId { get; set; }

    [Column("scan_task_id")]
    [ForeignKey(nameof(ScanTask))]
    public Guid ScanTaskId { get; set; }

    /// <summary>The S3 object key, as recorded in the manifest's file locations.</summary>
    [Column("file_location")]
    public string FileLocation { get; set; } = null!;

    [Column("status")]
    public string Status { get; set; } = null!;

    /// <summary>Failure reason, so the DLQ is a debugging aid rather than the record of what failed.</summary>
    [Column("error")]
    public string? Error { get; set; }

    [Column("created_at")]
    public DateTimeOffset CreatedAt { get; set; }

    [Column("updated_at")]
    public DateTimeOffset UpdatedAt { get; set; }

    public Scan Scan { get; set; } = null!;

    public ScanTask ScanTask { get; set; } = null!;
}

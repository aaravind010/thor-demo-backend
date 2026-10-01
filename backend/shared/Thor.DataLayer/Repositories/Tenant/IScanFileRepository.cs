using Thor.DataLayer.Models.Tenants;

namespace Thor.DataLayer.Repositories;

public interface IScanFileRepository : IRepository<ScanFile>
{
    /// <summary>
    /// Records every file a manifest listed as <see cref="ScanFileStatus.Received"/>, keeping any
    /// status already written for that (scan, file) — the list step is re-invoked on retry and on
    /// at-least-once SQS delivery, and must never reset a file that has since been ingested or
    /// failed.
    /// </summary>
    Task MarkReceivedAsync(IReadOnlyList<ScanFile> files, CancellationToken cancellationToken = default);

    /// <summary>
    /// Writes this file's outcome, overwriting whatever was there. Last write wins on purpose: a
    /// Map item that fails and is then retried successfully must end up Ingested, and status is a
    /// column keyed by file rather than a counter precisely so it can converge that way. Inserts the
    /// row if <see cref="MarkReceivedAsync"/> never ran for it.
    /// </summary>
    Task MarkStatusAsync(ScanFile file, CancellationToken cancellationToken = default);
}

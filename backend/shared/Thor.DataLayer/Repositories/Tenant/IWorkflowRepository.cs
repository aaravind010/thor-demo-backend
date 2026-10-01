using Thor.DataLayer.Models.Tenants;

namespace Thor.DataLayer.Repositories;

public interface IWorkflowRepository : IRepository<WorkflowEntity>
{
    Task<IReadOnlyList<WorkflowEntity>> GetByScanIdAsync(Guid scanId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<WorkflowEntity>> GetByScanManifestIdAsync(Guid scanManifestId, CancellationToken cancellationToken = default);

    Task<WorkflowEntity?> GetByRunIdAsync(Guid runId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Adds a line to this workflow's <c>error</c>, keeping what is already there. A single statement
    /// rather than a read-then-write because the callers are concurrent — a workflow that fans out
    /// over a Distributed Map can have several items failing at once, and a read-modify-write would
    /// let them overwrite each other's messages. Leaves <c>status</c> alone.
    /// </summary>
    Task AppendErrorAsync(Guid id, string error, CancellationToken cancellationToken = default);

    /// <summary>
    /// Puts this workflow back to <c>started</c> for a fresh attempt, clearing what the previous one
    /// recorded — its error and its completion time. A retry reuses the same row (the run id is
    /// derived, so a second execution finds the first one's), and a row still carrying the last
    /// attempt's failure describes neither attempt honestly.
    ///
    /// <para>For a workflow whose run is one execution, called once as it opens. Not for one that
    /// records progress across several steps of the same execution — this resets that progress.</para>
    /// </summary>
    Task ReopenAsync(Guid id, CancellationToken cancellationToken = default);
}

using Thor.DataLayer.Models.Tenants;

namespace Thor.DataLayer.Repositories;

public interface IOwnershipWalkCandidateRepository : IRepository<OwnershipWalkCandidate>
{
    /// <summary>
    /// Drops every walk row one run staged. Called as the run finalizes, once no vote chunk can read
    /// them any more — a failed run keeps its rows so a retry of the same run id resumes from them.
    /// </summary>
    Task<int> DeleteRunAsync(string runId, CancellationToken cancellationToken = default);
}

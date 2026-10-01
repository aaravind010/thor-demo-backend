using Microsoft.EntityFrameworkCore;
using Thor.DataLayer.Data;
using Thor.DataLayer.Models.Tenants;

namespace Thor.DataLayer.Repositories;

public sealed class OwnershipWalkCandidateRepository(TenantDbContext context)
    : Repository<OwnershipWalkCandidate>(context), IOwnershipWalkCandidateRepository
{
    public Task<int> DeleteRunAsync(string runId, CancellationToken cancellationToken = default) =>
        context.OwnershipWalkCandidates
            .Where(c => c.RunId == runId)
            .ExecuteDeleteAsync(cancellationToken);
}

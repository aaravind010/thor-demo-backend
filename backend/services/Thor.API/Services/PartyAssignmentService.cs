using Thor.Api.Models;
using Thor.DataConnectionManager;
using Thor.DataLayer.Models.Tenants;
using Thor.DataLayer.Repositories;

namespace Thor.Api.Services;

/// <summary>Backs <c>GET /party-assignments</c> and <c>GET /party-assignments/{id}</c>: reads <see cref="PartyAssignment"/> rows from the caller's tenant database.</summary>
public sealed class PartyAssignmentService(ITenantConnectionManager tenantConnectionManager)
{
    public async Task<CursorPage<PartyAssignmentResponse>> ListAsync(
        Guid tenantId, string? entityType, Guid? entityId, Guid? identityId, string? runId, bool? isActive,
        Guid? after, int limit, CancellationToken cancellationToken)
    {
        using var tenantDb = await tenantConnectionManager.GetTenantDbContextAsync(tenantId, cancellationToken);
        var page = await new PartyAssignmentRepository(tenantDb)
            .ListAsync(entityType, entityId, identityId, runId, isActive, after, limit, cancellationToken);
        return new CursorPage<PartyAssignmentResponse>(page.Items.Select(ToResponse).ToList(), page.NextCursor);
    }

    public async Task<PartyAssignmentResponse?> GetAsync(Guid tenantId, Guid id, CancellationToken cancellationToken)
    {
        using var tenantDb = await tenantConnectionManager.GetTenantDbContextAsync(tenantId, cancellationToken);
        var assignment = await new PartyAssignmentRepository(tenantDb).GetByIdAsync(id, cancellationToken);
        return assignment is null ? null : ToResponse(assignment);
    }

    private static PartyAssignmentResponse ToResponse(PartyAssignment assignment) => new(
        assignment.Id, assignment.EntityType, assignment.EntityId, assignment.IdentityId, assignment.Rank,
        assignment.IsActive, assignment.IsOverride, assignment.VoteDistribution, assignment.ContributingRuleIds,
        assignment.PrecisionScoreSnapshot, assignment.RunId, assignment.AssignedAt, assignment.OverriddenAt);
}

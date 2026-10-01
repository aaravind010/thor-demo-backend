using Microsoft.EntityFrameworkCore;
using Npgsql;
using Thor.Api.Exceptions;
using Thor.Api.Models;
using Thor.DataConnectionManager;
using Thor.DataLayer.Models.Tenants;
using Thor.DataLayer.Repositories;
using Thor.Rules.Ownership;

namespace Thor.Api.Services;

/// <summary>
/// Backs <c>POST /ownership-rules</c>, <c>GET /ownership-rules</c> and
/// <c>GET /ownership-rules/{id}</c>. A new rule is validated with the same
/// <see cref="OwnershipRuleDefinition"/> the Ownership workflow loads rules through, so the API
/// never stores a rule the engine would silently skip.
/// </summary>
public sealed class OwnershipRuleService(ITenantConnectionManager tenantConnectionManager)
{
    // The seeder inserts defaults with ON CONFLICT (rule_name) DO NOTHING, so a user rule taking one
    // of these names would silently stop that default from ever being seeded for the tenant.
    private static readonly HashSet<string> ReservedRuleNames =
        OwnershipRuleDefaults.All.Select(r => r.RuleName).ToHashSet(StringComparer.Ordinal);

    public async Task<OwnershipRuleResponse> CreateAsync(
        Guid tenantId, CreateOwnershipRuleRequest request, CancellationToken cancellationToken)
    {
        if (!OwnershipRuleType.All.Contains(request.RuleType))
        {
            throw new InvalidRuleDefinitionException(
                $"ruleType must be one of: {string.Join(", ", OwnershipRuleType.All)}.");
        }

        if (!OwnershipEntityTypes.All.Contains(request.AppliesTo))
        {
            throw new InvalidRuleDefinitionException(
                $"appliesTo must be one of: {string.Join(", ", OwnershipEntityTypes.All)}.");
        }

        if (!OwnershipRuleDefinition.TryParse(request.RuleType, request.AppliesTo, request.RuleDefinition, out _, out var error))
        {
            throw new InvalidRuleDefinitionException($"Invalid ruleDefinition: {error}");
        }

        if (ReservedRuleNames.Contains(request.RuleName))
        {
            throw new OwnershipRuleNameConflictException(request.RuleName);
        }

        using var tenantDb = await tenantConnectionManager.GetTenantDbContextAsync(tenantId, cancellationToken);
        var repository = new OwnershipRuleRepository(tenantDb);

        if (await repository.ExistsByNameAsync(request.RuleName, cancellationToken))
        {
            throw new OwnershipRuleNameConflictException(request.RuleName);
        }

        var now = DateTimeOffset.UtcNow;
        var rule = new OwnershipRule
        {
            Id = Guid.NewGuid(),
            RuleName = request.RuleName,
            RuleType = request.RuleType,
            RuleDefinition = request.RuleDefinition,
            AppliesTo = request.AppliesTo,
            IsActive = request.IsActive,
            PrecisionScore = request.PrecisionScore,
            CreatedAt = now,
            UpdatedAt = now,
        };

        await using var transaction = await tenantDb.Database.BeginTransactionAsync(cancellationToken);
        await repository.AddAsync(rule, cancellationToken);
        try
        {
            await tenantDb.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            // Lost a race with a concurrent create of the same name, past the pre-check above.
            throw new OwnershipRuleNameConflictException(request.RuleName);
        }

        await transaction.CommitAsync(cancellationToken);

        return ToResponse(rule);
    }

    public async Task<CursorPage<OwnershipRuleResponse>> ListAsync(
        Guid tenantId, string? ruleType, string? appliesTo, bool? isActive, Guid? after, int limit,
        CancellationToken cancellationToken)
    {
        using var tenantDb = await tenantConnectionManager.GetTenantDbContextAsync(tenantId, cancellationToken);
        var page = await new OwnershipRuleRepository(tenantDb)
            .ListAsync(ruleType, appliesTo, isActive, after, limit, cancellationToken);
        return new CursorPage<OwnershipRuleResponse>(page.Items.Select(ToResponse).ToList(), page.NextCursor);
    }

    public async Task<OwnershipRuleResponse?> GetAsync(Guid tenantId, Guid id, CancellationToken cancellationToken)
    {
        using var tenantDb = await tenantConnectionManager.GetTenantDbContextAsync(tenantId, cancellationToken);
        var rule = await new OwnershipRuleRepository(tenantDb).GetByIdAsync(id, cancellationToken);
        return rule is null ? null : ToResponse(rule);
    }

    private static OwnershipRuleResponse ToResponse(OwnershipRule rule) => new(
        rule.Id, rule.RuleName, rule.RuleType, rule.RuleDefinition, rule.AppliesTo, rule.IsActive,
        rule.TotalPredictions, rule.TruePositiveCount, rule.FalsePositiveCount, rule.PrecisionScore,
        rule.CreatedAt, rule.UpdatedAt);
}

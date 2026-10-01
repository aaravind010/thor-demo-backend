using Thor.Api.Exceptions;
using Thor.Api.Models;
using Thor.DataConnectionManager;
using Thor.DataLayer.Models.Tenants;
using Thor.DataLayer.Repositories;
using Thor.Rules.AccountType;

namespace Thor.Api.Services;

/// <summary>
/// Backs <c>POST /account-type-rules</c>, <c>GET /account-type-rules</c> and
/// <c>GET /account-type-rules/{id}</c>. A new rule is validated with the same
/// <see cref="AccountTypeRuleDefinition"/> the ATRE workflow loads rules through, so the API
/// never stores a rule ATRE would silently skip.
/// </summary>
public sealed class AccountTypeRuleService(ITenantConnectionManager tenantConnectionManager)
{
    public async Task<AccountTypeRuleResponse> CreateAsync(
        Guid tenantId, CreateAccountTypeRuleRequest request, CancellationToken cancellationToken)
    {
        if (request.AppliesTo != AccountTypeRuleDefinition.AccountAppliesTo)
        {
            throw new InvalidRuleDefinitionException(
                $"appliesTo must be '{AccountTypeRuleDefinition.AccountAppliesTo}'.");
        }

        if (!AccountTypeRuleDefinition.TryParse(request.RuleDefinition, out _, out var error))
        {
            throw new InvalidRuleDefinitionException($"Invalid ruleDefinition: {error}");
        }

        using var tenantDb = await tenantConnectionManager.GetTenantDbContextAsync(tenantId, cancellationToken);

        _ = await new AccountTypeRepository(tenantDb).GetByIdAsync(request.TargetAccountTypeId, cancellationToken)
            ?? throw new AccountTypeNotFoundException(request.TargetAccountTypeId);

        var now = DateTimeOffset.UtcNow;
        var rule = new AccountTypeRule
        {
            Id = Guid.NewGuid(),
            RuleName = request.RuleName,
            RuleDefinition = request.RuleDefinition,
            AppliesTo = request.AppliesTo,
            TargetAccountTypeId = request.TargetAccountTypeId,
            IsActive = request.IsActive,
            PrecisionScore = request.PrecisionScore,
            CreatedAt = now,
            UpdatedAt = now,
        };

        await using var transaction = await tenantDb.Database.BeginTransactionAsync(cancellationToken);
        await new AccountTypeRuleRepository(tenantDb).AddAsync(rule, cancellationToken);
        await tenantDb.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return ToResponse(rule);
    }

    public async Task<CursorPage<AccountTypeRuleResponse>> ListAsync(
        Guid tenantId, string? appliesTo, bool? isActive, Guid? targetAccountTypeId, Guid? after, int limit,
        CancellationToken cancellationToken)
    {
        using var tenantDb = await tenantConnectionManager.GetTenantDbContextAsync(tenantId, cancellationToken);
        var page = await new AccountTypeRuleRepository(tenantDb)
            .ListAsync(appliesTo, isActive, targetAccountTypeId, after, limit, cancellationToken);
        return new CursorPage<AccountTypeRuleResponse>(page.Items.Select(ToResponse).ToList(), page.NextCursor);
    }

    public async Task<AccountTypeRuleResponse?> GetAsync(Guid tenantId, Guid id, CancellationToken cancellationToken)
    {
        using var tenantDb = await tenantConnectionManager.GetTenantDbContextAsync(tenantId, cancellationToken);
        var rule = await new AccountTypeRuleRepository(tenantDb).GetByIdAsync(id, cancellationToken);
        return rule is null ? null : ToResponse(rule);
    }

    private static AccountTypeRuleResponse ToResponse(AccountTypeRule rule) => new(
        rule.Id, rule.RuleName, rule.RuleDefinition, rule.AppliesTo, rule.TargetAccountTypeId, rule.IsActive,
        rule.TotalPredictions, rule.TruePositiveCount, rule.FalsePositiveCount, rule.PrecisionScore,
        rule.CreatedAt, rule.UpdatedAt);
}

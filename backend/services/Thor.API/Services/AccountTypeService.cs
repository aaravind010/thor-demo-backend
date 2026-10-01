using Thor.Api.Models;
using Thor.DataConnectionManager;
using Thor.DataLayer.Models.Tenants;
using Thor.DataLayer.Repositories;

namespace Thor.Api.Services;

/// <summary>
/// Backs <c>POST /account-type</c>, <c>GET /account-type</c> and <c>GET /account-type/{id}</c>:
/// creates and reads <see cref="AccountType"/> rows in the caller's tenant database.
/// </summary>
public sealed class AccountTypeService(ITenantConnectionManager tenantConnectionManager)
{
    public async Task<AccountTypeResponse> CreateAsync(
        Guid tenantId, CreateAccountTypeRequest request, CancellationToken cancellationToken)
    {
        using var tenantDb = await tenantConnectionManager.GetTenantDbContextAsync(tenantId, cancellationToken);
        var accountTypeRepository = new AccountTypeRepository(tenantDb);

        var accountType = new AccountType
        {
            Id = Guid.NewGuid(),
            Name = request.Name,
            Description = request.Description,
            IsHuman = request.IsHuman,
        };

        await using var transaction = await tenantDb.Database.BeginTransactionAsync(cancellationToken);
        await accountTypeRepository.AddAsync(accountType, cancellationToken);
        await tenantDb.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return ToResponse(accountType);
    }

    public async Task<CursorPage<AccountTypeResponse>> ListAsync(
        Guid tenantId, Guid? after, int limit, CancellationToken cancellationToken)
    {
        using var tenantDb = await tenantConnectionManager.GetTenantDbContextAsync(tenantId, cancellationToken);
        var page = await new AccountTypeRepository(tenantDb).ListAsync(after, limit, cancellationToken);
        return new CursorPage<AccountTypeResponse>(page.Items.Select(ToResponse).ToList(), page.NextCursor);
    }

    public async Task<AccountTypeResponse?> GetAsync(Guid tenantId, Guid id, CancellationToken cancellationToken)
    {
        using var tenantDb = await tenantConnectionManager.GetTenantDbContextAsync(tenantId, cancellationToken);
        var accountType = await new AccountTypeRepository(tenantDb).GetByIdAsync(id, cancellationToken);
        return accountType is null ? null : ToResponse(accountType);
    }

    private static AccountTypeResponse ToResponse(AccountType accountType) => new(
        accountType.Id, accountType.Name, accountType.Description, accountType.IsHuman);
}

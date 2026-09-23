using Thor.Api.Models;
using Thor.DataConnectionManager;
using Thor.DataLayer.Models.Tenants;
using Thor.DataLayer.Repositories;

namespace Thor.Api.Services;

/// <summary>
/// Backs <c>POST /account-type</c>: creates an <see cref="AccountType"/> row in the caller's
/// tenant database.
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

    private static AccountTypeResponse ToResponse(AccountType accountType) => new(
        accountType.Id, accountType.Name, accountType.Description, accountType.IsHuman);
}

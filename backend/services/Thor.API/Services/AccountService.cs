using Thor.Api.Models;
using Thor.DataConnectionManager;
using Thor.DataLayer.Models.Tenants;
using Thor.DataLayer.Repositories;

namespace Thor.Api.Services;

/// <summary>Backs <c>GET /accounts</c> and <c>GET /accounts/{id}</c>: reads <see cref="Account"/> rows from the caller's tenant database.</summary>
public sealed class AccountService(ITenantConnectionManager tenantConnectionManager)
{
    public async Task<CursorPage<AccountResponse>> ListAsync(
        Guid tenantId, Guid? sourceId, Guid? accountTypeId, bool? isDeleted, Guid? after, int limit,
        CancellationToken cancellationToken)
    {
        using var tenantDb = await tenantConnectionManager.GetTenantDbContextAsync(tenantId, cancellationToken);
        var page = await new AccountRepository(tenantDb)
            .ListAsync(sourceId, accountTypeId, isDeleted, after, limit, cancellationToken);
        return new CursorPage<AccountResponse>(
            page.Items.Select(a => ToResponse(a, includeRawAttributes: false)).ToList(), page.NextCursor);
    }

    public async Task<AccountResponse?> GetAsync(Guid tenantId, Guid id, CancellationToken cancellationToken)
    {
        using var tenantDb = await tenantConnectionManager.GetTenantDbContextAsync(tenantId, cancellationToken);
        var account = await new AccountRepository(tenantDb).GetByIdAsync(id, cancellationToken);
        return account is null ? null : ToResponse(account, includeRawAttributes: true);
    }

    // raw_attributes can be large, so list pages leave it out and only the single-row read returns it.
    private static AccountResponse ToResponse(Account account, bool includeRawAttributes) => new(
        account.Id, account.SourceId, account.ConnectorType, account.NativeId, account.AccountKind, account.IsHuman,
        account.DisplayName, account.SamAccountName, account.Upn, account.Email, account.DomainName,
        account.FilerName, account.NativeAccountId, account.IsDeleted, account.IsDisabled, account.AccountTypeId,
        account.CreatedAt, account.UpdatedAt, includeRawAttributes ? account.RawAttributes : null);
}

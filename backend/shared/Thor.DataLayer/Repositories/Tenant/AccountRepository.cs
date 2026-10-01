using System.Text;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Thor.DataLayer.Data;
using Thor.DataLayer.Models.Tenants;

namespace Thor.DataLayer.Repositories;

public sealed class AccountRepository(TenantDbContext context)
    : Repository<Account>(context), IAccountRepository
{
    public async Task<IReadOnlyList<Account>> GetByIdsAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken = default) =>
        await context.Accounts.Where(a => ids.Contains(a.Id)).ToListAsync(cancellationToken);

    public async Task BulkUpdateAccountTypeAsync(
        IReadOnlyDictionary<Guid, Guid> accountTypeIdByAccountId, CancellationToken cancellationToken = default)
    {
        if (accountTypeIdByAccountId.Count == 0)
        {
            return;
        }

        var sql = new StringBuilder("""
            UPDATE tenant.account SET account_type_id = v.account_type_id, updated_at = now()
            FROM (VALUES

            """);
        var parameters = new List<NpgsqlParameter>();

        var i = 0;
        foreach (var (accountId, accountTypeId) in accountTypeIdByAccountId)
        {
            if (i > 0)
            {
                sql.Append(",\n");
            }

            sql.Append($"(@account_id{i}::uuid, @account_type_id{i}::uuid)");
            parameters.Add(new NpgsqlParameter($"account_id{i}", accountId));
            parameters.Add(new NpgsqlParameter($"account_type_id{i}", accountTypeId));
            i++;
        }

        sql.Append(") AS v(id, account_type_id)\nWHERE tenant.account.id = v.id");

        await TenantSqlExec.ExecAsync(context, sql.ToString(), parameters, cancellationToken);
    }

    public Task<KeysetPage<Account>> ListAsync(
        Guid? sourceId, Guid? accountTypeId, bool? isDeleted, Guid? after, int limit,
        CancellationToken cancellationToken = default) =>
        context.Accounts
            .AsNoTracking()
            .Where(a => sourceId == null || a.SourceId == sourceId)
            .Where(a => accountTypeId == null || a.AccountTypeId == accountTypeId)
            .Where(a => isDeleted == null || a.IsDeleted == isDeleted)
            .ToKeysetPageAsync(a => a.Id, after, limit, cancellationToken);
}

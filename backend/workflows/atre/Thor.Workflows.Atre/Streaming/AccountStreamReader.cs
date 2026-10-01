using Microsoft.EntityFrameworkCore;
using Thor.DataLayer.Data;
using Thor.DataLayer.Models.Tenants;
using Thor.Workflows.Atre.Constants;

namespace Thor.Workflows.Atre.Streaming;

/// <summary>
/// Streams one window of the accounts in scope for an ATRE run: manifest-scoped via an EXISTS join
/// against <c>ingest_change_event</c> when a manifest is given, or every account otherwise
/// (full-scan fallback). <c>AsNoTracking</c> + <c>AsAsyncEnumerable</c> so a chunk streams row by row
/// rather than materializing.
///
/// <para>The window is an offset and a limit over <c>ORDER BY id</c>, which is what makes the chunks
/// of a run disjoint and complete: the ordering is total and stable, so chunk <c>[0,10000)</c> and
/// chunk <c>[10000,20000)</c> cannot overlap or leave a gap. Ordering by anything ATRE itself writes
/// would not be stable — a run updates <c>account.account_type_id</c> as it goes — whereas <c>id</c>
/// is immutable and already the primary key.</para>
/// </summary>
internal sealed class AccountStreamReader(TenantDbContext context)
{
    /// <param name="offset">Rows to skip. Zero and a limit covering everything is the whole scope.</param>
    /// <param name="limit">Rows to read. A window past the end simply yields nothing, which is how a run discovers it is finished.</param>
    public IAsyncEnumerable<Account> StreamAsync(Guid? scanManifestId, int offset, int limit) =>
        Scope(scanManifestId)
            .OrderBy(account => account.Id)
            .Skip(offset)
            .Take(limit)
            .AsAsyncEnumerable();

    private IQueryable<Account> Scope(Guid? scanManifestId)
    {
        var accounts = context.Accounts.AsNoTracking();

        return scanManifestId is { } manifestId
            ? accounts.Where(account =>
                context.IngestChangeEvents.Any(changeEvent =>
                    changeEvent.ScanManifestId == manifestId &&
                    changeEvent.EntityType == ScopeConstants.AccountEntityType &&
                    changeEvent.EntityId == account.Id))
            : accounts;
    }
}

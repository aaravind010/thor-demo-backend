namespace Thor.DataLayer.Repositories;

/// <summary>
/// Write access to a staging table (<c>staging_account</c>, <c>staging_grp</c>, etc.). Not
/// an <see cref="IRepository{TEntity}"/> — staging tables are keyless (see ADR §12 Ingestion
/// &amp; CDC), so generic by-id lookups have no meaning here.
/// </summary>
public interface IStagingRepository<TEntity> where TEntity : class
{
    Task BulkInsertAsync(IEnumerable<TEntity> rows, CancellationToken cancellationToken = default);
}

using Thor.DataLayer.Models.Tenants;

namespace Thor.DataLayer.Repositories;

public interface IAccountRepository : IRepository<Account>
{
    Task<IReadOnlyList<Account>> GetByIdsAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken = default);
}

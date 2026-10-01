using Thor.DataLayer.Models.Tenants;

namespace Thor.DataLayer.Repositories;

public interface IAccountTypeRepository : IRepository<AccountType>
{
    Task<KeysetPage<AccountType>> ListAsync(Guid? after, int limit, CancellationToken cancellationToken = default);
}

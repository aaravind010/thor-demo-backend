using Thor.DataLayer.Models;

namespace Thor.DataLayer.Repositories;

public interface IAuthenticationFieldRepository : IRepository<AuthenticationField>
{
    Task<IReadOnlyList<AuthenticationField>> GetByTypeIdAsync(Guid typeId, CancellationToken cancellationToken = default);
}

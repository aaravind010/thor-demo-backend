using Thor.DataLayer.Models;

namespace Thor.DataLayer.Repositories;

public interface IAuthenticationTypeRepository : IRepository<AuthenticationType>
{
    /// <summary>Authentication types mapped to the given connector type.</summary>
    Task<IReadOnlyList<AuthenticationType>> GetByConnectorTypeAsync(short connectorType, CancellationToken cancellationToken = default);

    /// <summary>Whether the authentication type is mapped to the given connector type.</summary>
    Task<bool> SupportsConnectorTypeAsync(Guid typeId, short connectorType, CancellationToken cancellationToken = default);
}

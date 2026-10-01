using Thor.DataLayer.Repositories;

namespace Thor.Workflows.Ingestion;

/// <summary>
/// The connector-type ids this module dispatches on, resolved by name from the Master DB's
/// canonical <c>connector_types</c> table (see <see cref="ConnectorTypeRepository"/>) rather
/// than hardcoded.
/// </summary>
public sealed class ConnectorTypeCatalog(short activeDirectory, short cyberArk, short windows, short hrFeed)
{
    public short ActiveDirectory { get; } = activeDirectory;
    public short CyberArk { get; } = cyberArk;
    public short Windows { get; } = windows;
    public short HrFeed { get; } = hrFeed;

    public static async Task<ConnectorTypeCatalog> LoadAsync(IConnectorTypeRepository repository, CancellationToken cancellationToken = default)
    {
        var connectorTypes = await repository.GetAllAsync(cancellationToken);

        short Resolve(string name) =>
            connectorTypes.FirstOrDefault(c => c.Name == name)?.Id
                ?? throw new InvalidOperationException($"No connector type named '{name}' found in master.connector_types.");

        return new ConnectorTypeCatalog(
            Resolve("Active Directory"),
            Resolve("CyberArk"),
            Resolve("Windows Server"),
            Resolve("HR Feed"));
    }
}

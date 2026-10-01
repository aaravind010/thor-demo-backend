namespace Thor.Workflows.Ingestion.Tests;

/// <summary>
/// A fixed <see cref="ConnectorTypeCatalog"/> matching the real ids seeded into
/// <c>master.connector_types</c> (see <c>004_seed_connector_types.sql</c>), so tests don't need a
/// live Master DB to exercise connector-type dispatch.
/// </summary>
internal static class TestConnectorTypes
{
    public static readonly ConnectorTypeCatalog Catalog = new(activeDirectory: 1, cyberArk: 7, windows: 3, hrFeed: 17);
}

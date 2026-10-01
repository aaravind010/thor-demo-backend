using Microsoft.Extensions.Logging;
using Thor.Workflows.Ingestion.AttributeMapping;

namespace Thor.Workflows.Ingestion.Normalization;

/// <summary>
/// Resolves which <see cref="IConnectorNormalizer"/> handles a given connector type — the
/// dispatch point that replaces each step's former hardcoded <c>new AdNormalizer(...)</c> call.
/// </summary>
public static class ConnectorNormalizerFactory
{
    public static IConnectorNormalizer Create(short connectorType, ILoggerFactory loggerFactory, IAttributeMapProvider mapProvider, ConnectorTypeCatalog connectorTypes)
    {
        if (connectorType == connectorTypes.ActiveDirectory)
        {
            return new AdNormalizer(loggerFactory.CreateLogger<AdNormalizer>(), mapProvider, connectorTypes);
        }
        if (connectorType == connectorTypes.CyberArk)
        {
            return new CyberArkNormalizer(loggerFactory.CreateLogger<CyberArkNormalizer>(), mapProvider, connectorTypes);
        }
        if (connectorType == connectorTypes.Windows)
        {
            return new WindowsNormalizer(loggerFactory.CreateLogger<WindowsNormalizer>(), mapProvider, connectorTypes);
        }
        if (connectorType == connectorTypes.HrFeed)
        {
            return new HrFeedNormalizer(loggerFactory.CreateLogger<HrFeedNormalizer>(), mapProvider);
        }
        throw new InvalidOperationException($"No normalizer registered for connector type {connectorType}.");
    }
}

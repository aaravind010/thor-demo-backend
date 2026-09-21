using Microsoft.Extensions.Logging;
using Thor.Workflows.Ingestion.AttributeMapping;
using Thor.Workflows.Ingestion.Constants;

namespace Thor.Workflows.Ingestion.Normalization;

/// <summary>
/// Resolves which <see cref="IConnectorNormalizer"/> handles a given connector type — the
/// dispatch point that replaces each step's former hardcoded <c>new AdNormalizer(...)</c> call.
/// </summary>
public static class ConnectorNormalizerFactory
{
    public static IConnectorNormalizer Create(short connectorType, ILoggerFactory loggerFactory, IAttributeMapProvider mapProvider) => connectorType switch
    {
        ConnectorTypes.ActiveDirectory => new AdNormalizer(loggerFactory.CreateLogger<AdNormalizer>(), mapProvider),
        ConnectorTypes.CyberArk => new CyberArkNormalizer(loggerFactory.CreateLogger<CyberArkNormalizer>(), mapProvider),
        ConnectorTypes.Windows => new WindowsNormalizer(loggerFactory.CreateLogger<WindowsNormalizer>(), mapProvider),
        _ => throw new InvalidOperationException($"No normalizer registered for connector type {connectorType}."),
    };
}

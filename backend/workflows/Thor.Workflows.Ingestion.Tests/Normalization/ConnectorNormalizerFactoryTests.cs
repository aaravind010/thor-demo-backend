using Microsoft.Extensions.Logging.Abstractions;
using Thor.Workflows.Ingestion.AttributeMapping;
using Thor.Workflows.Ingestion.Constants;
using Thor.Workflows.Ingestion.Normalization;
using Xunit;

namespace Thor.Workflows.Ingestion.Tests.Normalization;

public class ConnectorNormalizerFactoryTests
{
    private static readonly IAttributeMapProvider MapProvider = new AttributeMapProvider();

    [Fact]
    public void Create_ActiveDirectory_ReturnsAdNormalizer()
    {
        var normalizer = ConnectorNormalizerFactory.Create(ConnectorTypes.ActiveDirectory, NullLoggerFactory.Instance, MapProvider);
        Assert.IsType<AdNormalizer>(normalizer);
    }

    [Fact]
    public void Create_CyberArk_ReturnsCyberArkNormalizer()
    {
        var normalizer = ConnectorNormalizerFactory.Create(ConnectorTypes.CyberArk, NullLoggerFactory.Instance, MapProvider);
        Assert.IsType<CyberArkNormalizer>(normalizer);
    }

    [Fact]
    public void Create_Windows_ReturnsWindowsNormalizer()
    {
        var normalizer = ConnectorNormalizerFactory.Create(ConnectorTypes.Windows, NullLoggerFactory.Instance, MapProvider);
        Assert.IsType<WindowsNormalizer>(normalizer);
    }

    [Fact]
    public void Create_UnregisteredConnectorType_Throws()
    {
        Assert.Throws<InvalidOperationException>(() => ConnectorNormalizerFactory.Create(999, NullLoggerFactory.Instance, MapProvider));
    }
}

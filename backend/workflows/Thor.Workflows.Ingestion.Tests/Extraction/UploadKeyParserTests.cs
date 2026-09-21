using Thor.S3;
using Xunit;

namespace Thor.Workflows.Ingestion.Tests.Extraction;

public sealed class UploadKeyParserTests
{
    [Fact]
    public void TryParse_ValidKey_ReturnsTenantSourceAndScanIds()
    {
        var tenantId = Guid.NewGuid();
        var sourceId = Guid.NewGuid();
        var scanId = Guid.NewGuid();

        var result = UploadKeyParser.TryParse($"tenants/{tenantId}/uploads/{scanId}/{sourceId}/export.zip");

        Assert.Equal((tenantId, sourceId, scanId), result);
    }

    [Fact]
    public void TryParse_TooFewSegments_ReturnsNull()
    {
        Assert.Null(UploadKeyParser.TryParse($"tenants/{Guid.NewGuid()}/uploads/{Guid.NewGuid()}/export.zip"));
    }

    [Fact]
    public void TryParse_WrongLiteralSegments_ReturnsNull()
    {
        Assert.Null(UploadKeyParser.TryParse($"foo/{Guid.NewGuid()}/bar/{Guid.NewGuid()}/{Guid.NewGuid()}/export.zip"));
    }

    [Fact]
    public void TryParse_NonGuidTenantSegment_ReturnsNull()
    {
        Assert.Null(UploadKeyParser.TryParse($"tenants/not-a-guid/uploads/{Guid.NewGuid()}/{Guid.NewGuid()}/export.zip"));
    }

    [Fact]
    public void TryParse_NonGuidSourceSegment_ReturnsNull()
    {
        Assert.Null(UploadKeyParser.TryParse($"tenants/{Guid.NewGuid()}/uploads/{Guid.NewGuid()}/not-a-guid/export.zip"));
    }

    [Fact]
    public void TryParse_NonGuidScanSegment_ReturnsNull()
    {
        Assert.Null(UploadKeyParser.TryParse($"tenants/{Guid.NewGuid()}/uploads/not-a-guid/{Guid.NewGuid()}/export.zip"));
    }

}

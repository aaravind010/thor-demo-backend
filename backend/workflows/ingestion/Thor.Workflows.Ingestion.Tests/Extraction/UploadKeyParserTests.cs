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

        Assert.Equal((tenantId, sourceId, scanId, (Guid?)null), result);
    }

    /// <summary>The shape Thor.TaskApi's UploadService actually mints, which is the only one carrying a task id.</summary>
    [Fact]
    public void TryParse_PresignedFileName_ReturnsTaskId()
    {
        var tenantId = Guid.NewGuid();
        var sourceId = Guid.NewGuid();
        var scanId = Guid.NewGuid();
        var taskId = Guid.NewGuid();

        var result = UploadKeyParser.TryParse(
            $"tenants/{tenantId}/uploads/{scanId}/{sourceId}/data_{taskId}_{sourceId}_{Guid.CreateVersion7()}");

        Assert.Equal((tenantId, sourceId, scanId, (Guid?)taskId), result);
    }

    [Theory]
    [InlineData("export.zip")]
    [InlineData("data_not-a-guid_x_y")]
    [InlineData("data_")]
    public void TryParse_FileNameWithoutATaskId_ParsesTheKeyWithANullTaskId(string fileName)
    {
        var tenantId = Guid.NewGuid();
        var sourceId = Guid.NewGuid();
        var scanId = Guid.NewGuid();

        var result = UploadKeyParser.TryParse($"tenants/{tenantId}/uploads/{scanId}/{sourceId}/{fileName}");

        Assert.NotNull(result);
        Assert.Null(result.Value.TaskId);
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

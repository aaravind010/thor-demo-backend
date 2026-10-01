using System.Text;
using Thor.Workflows.Ingestion.Extraction;
using Xunit;

namespace Thor.Workflows.Ingestion.Tests.Extraction;

/// <summary>
/// Covers <see cref="HrFeedExtractor"/>'s recognition of the connector's two upload shapes
/// (<c>FeedIngestorData</c> / <c>FeedIngestorPostData</c>) and its deliberate refusal to salvage
/// malformed JSON — a partial roster would wrongly deactivate people on promotion.
/// </summary>
public class HrFeedExtractorTests
{
    [Fact]
    public void Extract_FeedIngestorData_ReturnsUserSpecs()
    {
        var bytes = Encoding.UTF8.GetBytes("""
            {"UserSpecs":[{"EmployeeID":"E1"},{"EmployeeID":"E2"}],"Targets":null,"RetireOwner":"False","TaskId":-1}
            """);

        var export = HrFeedExtractor.Extract(bytes);

        Assert.False(export.IsPostData);
        Assert.Equal(2, export.UserSpecs.Count);
        Assert.Equal(0, export.SkippedCount);
    }

    [Fact]
    public void Extract_StripsLeadingUtf8Bom()
    {
        var withBom = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes("""{"UserSpecs":[{"EmployeeID":"E1"}]}""")).ToArray();

        var export = HrFeedExtractor.Extract(withBom);

        Assert.Single(export.UserSpecs);
    }

    [Fact]
    public void Extract_FeedIngestorPostData_IsRecognizedAsPostData()
    {
        var bytes = Encoding.UTF8.GetBytes("""
            {"AppUsers":null,"RawFeedData":",\nFull Name,Employee ID (Unique ID)\nJane Doe,E20481","SetPendingReportingStatus":true,"TaskId":-1}
            """);

        var export = HrFeedExtractor.Extract(bytes);

        Assert.True(export.IsPostData);
        Assert.Empty(export.UserSpecs);
    }

    [Fact]
    public void Extract_EmptyUserSpecs_ReturnsNoPeople()
    {
        var export = HrFeedExtractor.Extract(Encoding.UTF8.GetBytes("""{"UserSpecs":[]}"""));

        Assert.False(export.IsPostData);
        Assert.Empty(export.UserSpecs);
    }

    [Fact]
    public void Extract_NonObjectUserSpecEntries_AreSkippedAndCounted()
    {
        var export = HrFeedExtractor.Extract(Encoding.UTF8.GetBytes("""{"UserSpecs":[{"EmployeeID":"E1"},"junk",null]}"""));

        Assert.Single(export.UserSpecs);
        Assert.Equal(2, export.SkippedCount);
    }

    [Theory]
    [InlineData("""{"UserSpecs":[{"EmployeeID":"E1"},""")]
    [InlineData("""{"UserSpecs":[{"EmployeeID":"E1"},],}""")]
    public void Extract_MalformedJson_Throws(string json)
    {
        Assert.Throws<InvalidOperationException>(() => HrFeedExtractor.Extract(Encoding.UTF8.GetBytes(json)));
    }

    [Theory]
    [InlineData("""[{"EmployeeID":"E1"}]""")]
    [InlineData("""{"UserSpecs":null}""")]
    [InlineData("""{"Domains":[]}""")]
    public void Extract_UnrecognizedEnvelope_Throws(string json)
    {
        Assert.Throws<InvalidOperationException>(() => HrFeedExtractor.Extract(Encoding.UTF8.GetBytes(json)));
    }
}

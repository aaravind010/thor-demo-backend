using System.Text;
using System.Text.Json.Nodes;
using Thor.Workflows.Ingestion.Extraction;
using Xunit;

namespace Thor.Workflows.Ingestion.Tests.Extraction;

/// <summary>
/// Covers <see cref="WindowsExtractor"/>'s root-document recovery (mirrors
/// <see cref="CyberArkExtractor"/>'s) plus its AD-parallel per-<c>LocalADPaths[]</c>-entry
/// recovery, mirroring the equivalent <c>AccountsPaths</c> coverage in <c>AdExtractorTests</c>.
/// </summary>
public class WindowsExtractorTests
{
    private static byte[] BuildRootBytes(params string[] localAdPathsJson)
    {
        var paths = string.Join(",", localAdPathsJson.Select(p => JsonValue.Create(p)!.ToJsonString()));
        var doc = $$"""{"Filers":[{"LocalADPaths":[{{paths}}]}]}""";
        return Encoding.UTF8.GetBytes(doc);
    }

    [Fact]
    public void Extract_ParsesCleanJson()
    {
        var innerJson = """{"Accounts":[{"SamAccountName":"u"}]}""";
        var bytes = BuildRootBytes(innerJson);

        var export = WindowsExtractor.Extract(bytes);

        Assert.Equal(0, export.RepairedCount);
        Assert.Equal(0, export.SkippedCount);
        var paths = (JsonArray)((JsonObject)((JsonArray)export.Root["Filers"]!)[0]!)["LocalADPaths"]!;
        Assert.Single(paths);
    }

    [Fact]
    public void Extract_StripsLeadingUtf8Bom()
    {
        var withoutBom = BuildRootBytes("""{"Accounts":[]}""");
        var withBom = Encoding.UTF8.GetPreamble().Concat(withoutBom).ToArray();

        var export = WindowsExtractor.Extract(withBom);

        Assert.True(export.Root.ContainsKey("Filers"));
    }

    [Fact]
    public void Extract_RecoversRootDocumentWithTrailingComma()
    {
        var bytes = Encoding.UTF8.GetBytes("""{"Filers":[],}""");

        var export = WindowsExtractor.Extract(bytes);

        Assert.Equal(1, export.RepairedCount);
    }

    [Fact]
    public void Extract_ThrowsOnUnrecoverableRootDocument()
    {
        var bytes = Encoding.UTF8.GetBytes("""{"Filers":undefined}""");

        Assert.Throws<InvalidOperationException>(() => WindowsExtractor.Extract(bytes));
    }

    [Fact]
    public void Extract_RecoversMalformedLocalAdPathEntry_AndReencodesItCleanly()
    {
        var malformed = """{"Accounts":[{"SamAccountName":"a"}],}"""; // trailing comma
        var bytes = BuildRootBytes(malformed);

        var export = WindowsExtractor.Extract(bytes);

        Assert.Equal(1, export.RepairedCount);
        Assert.Equal(0, export.SkippedCount);
        var paths = (JsonArray)((JsonObject)((JsonArray)export.Root["Filers"]!)[0]!)["LocalADPaths"]!;
        var reencoded = paths[0]!.GetValue<string>();
        var inner = (JsonObject)JsonNode.Parse(reencoded)!; // must be plain-parseable now
        Assert.Single((JsonArray)inner["Accounts"]!);
    }

    [Fact]
    public void Extract_DropsUnrecoverableLocalAdPathEntry_ButKeepsValidSibling()
    {
        var valid = """{"Accounts":[{"SamAccountName":"a"}]}""";
        var garbage = """{"Accounts":undefined}""";
        var bytes = BuildRootBytes(garbage, valid);

        var export = WindowsExtractor.Extract(bytes);

        Assert.Equal(1, export.SkippedCount);
        var paths = (JsonArray)((JsonObject)((JsonArray)export.Root["Filers"]!)[0]!)["LocalADPaths"]!;
        Assert.Single(paths);
    }
}

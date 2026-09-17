using System.Text;
using System.Text.Json.Nodes;
using Thor.Workflows.Ingestion.Extraction;
using Xunit;

namespace Thor.Workflows.Ingestion.Tests.Extraction;

/// <summary>
/// Covers <see cref="CyberArkExtractor"/>'s BOM-stripping and malformed-root-JSON recovery —
/// CyberArk has no nested double-encoded structure, so this is the whole extraction surface.
/// </summary>
public class CyberArkExtractorTests
{
    [Fact]
    public void Extract_ParsesCleanJson()
    {
        var bytes = Encoding.UTF8.GetBytes("""{"Safes":[],"Accounts":{"AccountDetails":[]},"Members":[]}""");

        var export = CyberArkExtractor.Extract(bytes);

        Assert.Equal(0, export.RepairedCount);
        Assert.True(export.Root.ContainsKey("Safes"));
    }

    [Fact]
    public void Extract_StripsLeadingUtf8Bom()
    {
        var withoutBom = Encoding.UTF8.GetBytes("""{"Safes":[]}""");
        var withBom = Encoding.UTF8.GetPreamble().Concat(withoutBom).ToArray();

        var export = CyberArkExtractor.Extract(withBom);

        Assert.True(export.Root.ContainsKey("Safes"));
    }

    [Fact]
    public void Extract_RecoversRootDocumentWithTrailingComma()
    {
        var bytes = Encoding.UTF8.GetBytes("""{"Safes":[],}""");

        var export = CyberArkExtractor.Extract(bytes);

        Assert.Equal(1, export.RepairedCount);
        Assert.True(export.Root.ContainsKey("Safes"));
    }

    [Fact]
    public void Extract_RecoversTruncatedRootDocument()
    {
        var bytes = Encoding.UTF8.GetBytes("""{"Safes":[{"SafeNumber":1}],"Accounts":{"AccountDetails":[""");

        var export = CyberArkExtractor.Extract(bytes);

        Assert.Equal(1, export.RepairedCount);
        Assert.Single((JsonArray)export.Root["Safes"]!);
    }

    [Fact]
    public void Extract_ThrowsOnUnrecoverableRootDocument()
    {
        var bytes = Encoding.UTF8.GetBytes("""{"Safes":undefined}""");

        Assert.Throws<InvalidOperationException>(() => CyberArkExtractor.Extract(bytes));
    }
}

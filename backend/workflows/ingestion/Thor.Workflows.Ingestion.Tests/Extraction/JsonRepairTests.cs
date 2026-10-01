using System.Text;
using System.Text.Json.Nodes;
using Thor.Workflows.Ingestion.Extraction;
using Xunit;

namespace Thor.Workflows.Ingestion.Tests.Extraction;

/// <summary>
/// Direct coverage of <see cref="JsonRepair"/>'s recovery heuristics, independent of how
/// <see cref="AdExtractor"/> uses them.
/// </summary>
public class JsonRepairTests
{
    private static (JsonNode? Node, int BytesConsumed) TryParseBytes(string json) =>
        JsonRepair.TryParse(Encoding.UTF8.GetBytes(json));

    [Fact]
    public void TryParse_TrailingComma_Recovers()
    {
        var (node, consumed) = TryParseBytes("""{"a":1,}""");

        Assert.NotNull(node);
        Assert.Equal(1, node!["a"]!.GetValue<int>());
        Assert.Equal(Encoding.UTF8.GetByteCount("""{"a":1,}"""), consumed);
    }

    [Fact]
    public void TryParse_TrailingComment_Recovers()
    {
        var json = "{\"a\":1} // trailing comment";
        var (node, _) = TryParseBytes(json);

        Assert.NotNull(node);
        Assert.Equal(1, node!["a"]!.GetValue<int>());
    }

    [Fact]
    public void TryParse_TruncatedMidArray_RecoversCompleteElements()
    {
        var (node, _) = TryParseBytes("""{"Domains":[{"a":1},{"b":2""");

        Assert.NotNull(node);
        var domains = (JsonArray)node!["Domains"]!;
        Assert.Equal(2, domains.Count);
        Assert.Equal(1, domains[0]!["a"]!.GetValue<int>());
        Assert.Equal(2, domains[1]!["b"]!.GetValue<int>());
    }

    [Fact]
    public void TryParse_TruncatedRightAfterComma_DropsIncompleteTrailingElement()
    {
        var (node, _) = TryParseBytes("""{"Domains":[{"a":1},""");

        Assert.NotNull(node);
        var domains = (JsonArray)node!["Domains"]!;
        Assert.Single(domains);
        Assert.Equal(1, domains[0]!["a"]!.GetValue<int>());
    }

    [Fact]
    public void TryParse_TruncatedRightAfterDanglingKey_DropsIncompleteTrailingField()
    {
        var (node, _) = TryParseBytes("""{"a":1,"b":""");

        Assert.NotNull(node);
        Assert.Equal(1, node!["a"]!.GetValue<int>());
        Assert.False(((JsonObject)node!).ContainsKey("b"));
    }

    [Fact]
    public void TryParse_TruncatedMidString_DoesNotThrow_AndRecoversPrecedingFields()
    {
        var (node, _) = TryParseBytes("""{"a":1,"b":"unterm""");

        Assert.NotNull(node);
        Assert.Equal(1, node!["a"]!.GetValue<int>());
    }

    [Fact]
    public void TryParse_InvalidTokenInsideOtherwiseWellBracketedDocument_ReportsUnrecoverableAtBoundary()
    {
        var json = """{"a":undefined}""";
        var (node, consumed) = TryParseBytes(json);

        Assert.Null(node);
        Assert.Equal(Encoding.UTF8.GetByteCount(json), consumed);
    }

    [Fact]
    public void TryParse_NonJsonGarbage_IsUnrecoverable()
    {
        var (node, _) = TryParseBytes("not json at all");

        Assert.Null(node);
    }

    [Fact]
    public void TryParse_StringOverload_RecoversTruncatedJson()
    {
        var node = JsonRepair.TryParse("""{"Domain":"test.com","Accounts":[{"a":1}]""");

        Assert.NotNull(node);
        Assert.Single((JsonArray)node!["Accounts"]!);
    }

    [Fact]
    public void TryParse_StringOverload_NonJsonGarbage_ReturnsNull()
    {
        var node = JsonRepair.TryParse("not json at all");

        Assert.Null(node);
    }

    [Fact]
    public void TryParseObject_ValidJson_ReturnsDirectly_NotRepaired()
    {
        var (root, repaired) = JsonRepair.TryParseObject(Encoding.UTF8.GetBytes("""{"a":1}"""));

        Assert.NotNull(root);
        Assert.False(repaired);
        Assert.Equal(1, root!["a"]!.GetValue<int>());
    }

    [Fact]
    public void TryParseObject_TrailingComma_RecoversAndReportsRepaired()
    {
        var (root, repaired) = JsonRepair.TryParseObject(Encoding.UTF8.GetBytes("""{"a":1,}"""));

        Assert.NotNull(root);
        Assert.True(repaired);
    }

    [Fact]
    public void TryParseObject_Unrecoverable_ReturnsNull_NotRepaired()
    {
        var (root, repaired) = JsonRepair.TryParseObject(Encoding.UTF8.GetBytes("""{"a":undefined}"""));

        Assert.Null(root);
        Assert.False(repaired);
    }

    [Fact]
    public void TryParseObject_StringOverload_ValidJson_ReturnsDirectly_NotRepaired()
    {
        var (node, repaired) = JsonRepair.TryParseObject("""{"a":1}""");

        Assert.NotNull(node);
        Assert.False(repaired);
    }

    [Fact]
    public void TryParseObject_StringOverload_TruncatedJson_RecoversAndReportsRepaired()
    {
        var (node, repaired) = JsonRepair.TryParseObject("""{"Domain":"test.com","Accounts":[{"a":1}]""");

        Assert.NotNull(node);
        Assert.True(repaired);
        Assert.Single((JsonArray)node!["Accounts"]!);
    }

    [Fact]
    public void TryParseObject_StringOverload_NonJsonGarbage_ReturnsNull_NotRepaired()
    {
        var (node, repaired) = JsonRepair.TryParseObject("not json at all");

        Assert.Null(node);
        Assert.False(repaired);
    }
}

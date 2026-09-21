using Thor.Graph;
using Thor.Graph.BulkLoad;
using Xunit;

namespace Thor.Workflows.Ingestion.Tests.Graph;

public sealed class GremlinCsvWriterTests
{
    [Fact]
    public void WriteVertexCsv_EmptyList_ReturnsEmptyString()
    {
        Assert.Equal(string.Empty, GremlinCsvWriter.WriteVertexCsv(Guid.NewGuid(), "account", []));
    }

    [Fact]
    public void WriteVertexCsv_OneRow_WritesHeaderAndTenantNamespacedId()
    {
        var tenantId = Guid.NewGuid();
        var entityId = Guid.NewGuid();
        var properties = new Dictionary<string, object?> { ["displayName"] = "Alice", ["isDisabled"] = false };

        var csv = GremlinCsvWriter.WriteVertexCsv(tenantId, "account", [(entityId, properties)]);
        var lines = csv.TrimEnd('\n').Split('\n');

        Assert.Equal("~id,~label,tenantId:String(single),pgId:String(single),displayName:String(single),isDisabled:Bool(single)", lines[0]);
        Assert.Equal(
            $"{tenantId:N}:account:{entityId:N},account,{tenantId},{entityId},Alice,false",
            lines[1]);
    }

    [Fact]
    public void WriteVertexCsv_ValueContainingComma_IsQuoted()
    {
        var tenantId = Guid.NewGuid();
        var entityId = Guid.NewGuid();
        var properties = new Dictionary<string, object?> { ["displayName"] = "Doe, Jane" };

        var csv = GremlinCsvWriter.WriteVertexCsv(tenantId, "account", [(entityId, properties)]);
        var lines = csv.TrimEnd('\n').Split('\n');

        Assert.Contains("\"Doe, Jane\"", lines[1]);
    }

    [Fact]
    public void WriteVertexCsv_NullPropertyValue_WritesEmptyField()
    {
        var tenantId = Guid.NewGuid();
        var entityId = Guid.NewGuid();
        var properties = new Dictionary<string, object?> { ["email"] = null };

        var csv = GremlinCsvWriter.WriteVertexCsv(tenantId, "account", [(entityId, properties)]);
        var lines = csv.TrimEnd('\n').Split('\n');

        Assert.EndsWith(",", lines[1]);
    }

    [Fact]
    public void WriteVertexCsv_HeterogeneousPropertyKeySets_ThrowsInvalidOperationException()
    {
        var tenantId = Guid.NewGuid();
        var firstId = Guid.NewGuid();
        var secondId = Guid.NewGuid();
        var vertices = new List<(Guid, IReadOnlyDictionary<string, object?>)>
        {
            (firstId, new Dictionary<string, object?> { ["displayName"] = "Alice" }),
            (secondId, new Dictionary<string, object?> { ["displayName"] = "Bob", ["email"] = "bob@test.com" }),
        };

        var ex = Assert.Throws<InvalidOperationException>(() => GremlinCsvWriter.WriteVertexCsv(tenantId, "account", vertices));
        Assert.Contains(secondId.ToString(), ex.Message);
    }

    [Fact]
    public void WriteVertexCsv_PropertyKeyContainingComma_HeaderColumnIsQuoted()
    {
        var tenantId = Guid.NewGuid();
        var entityId = Guid.NewGuid();
        var properties = new Dictionary<string, object?> { ["display,Name"] = "Alice" };

        var csv = GremlinCsvWriter.WriteVertexCsv(tenantId, "account", [(entityId, properties)]);
        var lines = csv.TrimEnd('\n').Split('\n');

        Assert.Contains("\"display,Name:String(single)\"", lines[0]);
    }

    [Fact]
    public void WriteVertexCsv_PropertyKeyContainingQuote_HeaderColumnIsQuotedAndDoubled()
    {
        var tenantId = Guid.NewGuid();
        var entityId = Guid.NewGuid();
        var properties = new Dictionary<string, object?> { ["display\"Name"] = "Alice" };

        var csv = GremlinCsvWriter.WriteVertexCsv(tenantId, "account", [(entityId, properties)]);
        var lines = csv.TrimEnd('\n').Split('\n');

        Assert.Contains("\"display\"\"Name:String(single)\"", lines[0]);
    }

    [Fact]
    public void WriteVertexCsv_PropertyKeyContainingNewline_HeaderColumnIsQuoted()
    {
        var tenantId = Guid.NewGuid();
        var entityId = Guid.NewGuid();
        var properties = new Dictionary<string, object?> { ["display\nName"] = "Alice" };

        var csv = GremlinCsvWriter.WriteVertexCsv(tenantId, "account", [(entityId, properties)]);

        Assert.Contains("\"display\nName:String(single)\"", csv);
    }

    [Fact]
    public void WriteVertexCsv_PropertyKeyContainingColon_ThrowsInvalidOperationException()
    {
        var tenantId = Guid.NewGuid();
        var entityId = Guid.NewGuid();
        var properties = new Dictionary<string, object?> { ["display:Name"] = "Alice" };

        var ex = Assert.Throws<InvalidOperationException>(
            () => GremlinCsvWriter.WriteVertexCsv(tenantId, "account", [(entityId, properties)]));
        Assert.Contains("display:Name", ex.Message);
    }

    [Fact]
    public void WriteEdgeCsv_EmptyList_ReturnsEmptyString()
    {
        Assert.Equal(string.Empty, GremlinCsvWriter.WriteEdgeCsv(Guid.NewGuid(), []));
    }

    [Fact]
    public void WriteEdgeCsv_OneEdge_WritesFromToLabelAndTenantNamespacedIds()
    {
        var tenantId = Guid.NewGuid();
        var edgeId = Guid.NewGuid();
        var fromId = Guid.NewGuid();
        var toId = Guid.NewGuid();
        var from = new GraphVertexRef("account", fromId);
        var to = new GraphVertexRef("grp", toId);
        var noProps = new Dictionary<string, object?>();

        var csv = GremlinCsvWriter.WriteEdgeCsv(tenantId, [(edgeId, "memberOf", from, to, noProps)]);
        var lines = csv.TrimEnd('\n').Split('\n');

        Assert.Equal("~id,~from,~to,~label,tenantId:String(single)", lines[0]);
        Assert.Equal(
            $"{tenantId:N}:edge:{edgeId:N},{tenantId:N}:account:{fromId:N},{tenantId:N}:grp:{toId:N},memberOf,{tenantId}",
            lines[1]);
    }

    [Fact]
    public void WriteEdgeCsv_EdgeWithProperties_AddsUnionedPropertyColumns()
    {
        var tenantId = Guid.NewGuid();
        var from = new GraphVertexRef("account", Guid.NewGuid());
        var to = new GraphVertexRef("asset", Guid.NewGuid());
        var hasAccessProps = new Dictionary<string, object?> { ["isAdmin"] = true, ["manageSafe"] = false };
        var memberOfProps = new Dictionary<string, object?>();

        var edges = new List<(Guid, string, GraphVertexRef, GraphVertexRef, IReadOnlyDictionary<string, object?>)>
        {
            (Guid.NewGuid(), "HAS_ACCESS", from, to, hasAccessProps),
            (Guid.NewGuid(), "MEMBER_OF", from, to, memberOfProps),
        };

        var csv = GremlinCsvWriter.WriteEdgeCsv(tenantId, edges);
        var lines = csv.TrimEnd('\n').Split('\n');

        Assert.Equal("~id,~from,~to,~label,tenantId:String(single),isAdmin:Bool(single),manageSafe:Bool(single)", lines[0]);
        Assert.EndsWith(",true,false", lines[1]);
        Assert.EndsWith(",,", lines[2]); // MEMBER_OF has neither property — empty cells, not dropped columns
    }

    [Fact]
    public void WriteEdgeCsv_PropertyKeyContainingColon_ThrowsInvalidOperationException()
    {
        var tenantId = Guid.NewGuid();
        var from = new GraphVertexRef("account", Guid.NewGuid());
        var to = new GraphVertexRef("asset", Guid.NewGuid());
        var props = new Dictionary<string, object?> { ["is:admin"] = true };
        var edges = new List<(Guid, string, GraphVertexRef, GraphVertexRef, IReadOnlyDictionary<string, object?>)>
        {
            (Guid.NewGuid(), "HAS_ACCESS", from, to, props),
        };

        var ex = Assert.Throws<InvalidOperationException>(() => GremlinCsvWriter.WriteEdgeCsv(tenantId, edges));
        Assert.Contains("is:admin", ex.Message);
    }
}

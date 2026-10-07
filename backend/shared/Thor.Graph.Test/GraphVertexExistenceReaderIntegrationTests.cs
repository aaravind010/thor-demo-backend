using Thor.Graph.Test.Fixtures;

namespace Thor.Graph.Test;

/// <summary>Runs the reader against a real Gremlin Server (Docker required), seeding vertices through the production <see cref="GraphVertexStore"/>.</summary>
public sealed class GraphVertexExistenceReaderIntegrationTests(GremlinServerFixture fixture) : IClassFixture<GremlinServerFixture>
{
    private static readonly IReadOnlyDictionary<string, object?> NoProps = new Dictionary<string, object?>();

    private GraphVertexExistenceReader Reader => new(fixture.Client);

    [Fact]
    public async Task GetExistingAsync_ReturnsOnlyVerticesPresentForTheTenant()
    {
        var tenantId = Guid.NewGuid();
        var otherTenantId = Guid.NewGuid();
        var present = Guid.NewGuid();
        var absent = Guid.NewGuid();
        var otherTenantOnly = Guid.NewGuid();

        var vertices = new GraphVertexStore(fixture.Client);
        await vertices.UpsertVertexAsync(tenantId, "account", present, NoProps);
        await vertices.UpsertVertexAsync(otherTenantId, "grp", otherTenantOnly, NoProps);

        var existing = await Reader.GetExistingAsync(tenantId,
        [
            new GraphVertexRef("account", present),
            new GraphVertexRef("account", absent),
            new GraphVertexRef("grp", otherTenantOnly),
        ]);

        Assert.Equal([new GraphVertexRef("account", present)], existing);
    }

    [Fact]
    public async Task GetExistingAsync_MoreThanOneBatch_ChecksEveryVertex()
    {
        var tenantId = Guid.NewGuid();
        var ids = Enumerable.Range(0, 1100).Select(_ => Guid.NewGuid()).ToList();
        var seeded = ids.Take(600).ToList();

        await new GraphVertexStore(fixture.Client).UpsertVerticesAsync(tenantId, "account", seeded.Select(id => (id, NoProps)).ToList());

        var existing = await Reader.GetExistingAsync(tenantId, ids.Select(id => new GraphVertexRef("account", id)).ToList());

        Assert.Equal(600, existing.Count);
    }

    [Fact]
    public async Task GetExistingAsync_NoVertices_ReturnsEmpty()
    {
        var existing = await Reader.GetExistingAsync(Guid.NewGuid(), []);

        Assert.Empty(existing);
    }
}

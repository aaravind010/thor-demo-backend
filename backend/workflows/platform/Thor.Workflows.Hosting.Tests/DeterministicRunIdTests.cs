using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Thor.Workflows.Hosting.Tests;

public sealed class DeterministicRunIdTests
{
    private static readonly Guid NamespaceA = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid NamespaceB = Guid.Parse("22222222-2222-2222-2222-222222222222");

    [Fact]
    public void Derive_SameTenantManifestAndNamespace_ProducesSameRunId()
    {
        var tenantId = Guid.NewGuid();
        var manifestId = Guid.NewGuid();

        var first = DeterministicRunId.Derive(tenantId, manifestId, null, NamespaceA, NullLogger.Instance);
        var second = DeterministicRunId.Derive(tenantId, manifestId, null, NamespaceA, NullLogger.Instance);

        Assert.Equal(first, second);
    }

    [Fact]
    public void Derive_DifferentManifest_ProducesDifferentRunId()
    {
        var tenantId = Guid.NewGuid();

        var first = DeterministicRunId.Derive(tenantId, Guid.NewGuid(), null, NamespaceA, NullLogger.Instance);
        var second = DeterministicRunId.Derive(tenantId, Guid.NewGuid(), null, NamespaceA, NullLogger.Instance);

        Assert.NotEqual(first, second);
    }

    [Fact]
    public void Derive_DifferentTenant_ProducesDifferentRunId()
    {
        var manifestId = Guid.NewGuid();

        var first = DeterministicRunId.Derive(Guid.NewGuid(), manifestId, null, NamespaceA, NullLogger.Instance);
        var second = DeterministicRunId.Derive(Guid.NewGuid(), manifestId, null, NamespaceA, NullLogger.Instance);

        Assert.NotEqual(first, second);
    }

    [Fact]
    public void Derive_DifferentNamespace_ProducesDifferentRunId()
    {
        var tenantId = Guid.NewGuid();
        var manifestId = Guid.NewGuid();

        var first = DeterministicRunId.Derive(tenantId, manifestId, null, NamespaceA, NullLogger.Instance);
        var second = DeterministicRunId.Derive(tenantId, manifestId, null, NamespaceB, NullLogger.Instance);

        Assert.NotEqual(first, second);
    }

    [Fact]
    public void Derive_ExplicitRunId_AlwaysWins()
    {
        var explicitRunId = Guid.NewGuid();
        var result = DeterministicRunId.Derive(Guid.NewGuid(), Guid.NewGuid(), explicitRunId, NamespaceA, NullLogger.Instance);
        Assert.Equal(explicitRunId, result);
    }

    [Fact]
    public void Derive_NoManifestNoExplicitRunId_ProducesRandomRunIdEachTime()
    {
        var tenantId = Guid.NewGuid();
        var first = DeterministicRunId.Derive(tenantId, null, null, NamespaceA, NullLogger.Instance);
        var second = DeterministicRunId.Derive(tenantId, null, null, NamespaceA, NullLogger.Instance);
        Assert.NotEqual(first, second);
    }
}

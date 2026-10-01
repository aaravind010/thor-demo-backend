using Microsoft.Extensions.Logging.Abstractions;
using Thor.Workflows.Atre.Persistence;
using Xunit;

namespace Thor.Workflows.Atre.Tests.Persistence;

/// <summary>Wiring tests for the ATRE-specific wrapper; see DeterministicRunIdTests for algorithm coverage.</summary>
public sealed class AtreRunIdentityTests
{
    [Fact]
    public void Derive_SameTenantAndManifest_ProducesSameRunId()
    {
        var tenantId = Guid.NewGuid();
        var manifestId = Guid.NewGuid();

        var first = AtreRunIdentity.Derive(tenantId, manifestId, null, NullLogger.Instance);
        var second = AtreRunIdentity.Derive(tenantId, manifestId, null, NullLogger.Instance);

        Assert.Equal(first, second);
    }

    [Fact]
    public void Derive_ExplicitRunId_AlwaysWins()
    {
        var explicitRunId = Guid.NewGuid();
        var result = AtreRunIdentity.Derive(Guid.NewGuid(), Guid.NewGuid(), explicitRunId, NullLogger.Instance);
        Assert.Equal(explicitRunId, result);
    }
}

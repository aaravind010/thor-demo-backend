using Microsoft.Extensions.Logging.Abstractions;
using Thor.Workflows.Ownership.Persistence;
using Xunit;

namespace Thor.Workflows.Ownership.Tests.Persistence;

/// <summary>
/// Wiring tests only — <see cref="OwnershipRunIdentity"/> is a thin wrapper over
/// <see cref="Thor.Workflows.Hosting.DeterministicRunId"/>; the algorithm itself is covered
/// by <c>DeterministicRunIdTests</c> in <c>Thor.Workflows.Hosting.Tests</c>.
/// </summary>
public sealed class OwnershipRunIdentityTests
{
    [Fact]
    public void Derive_SameTenantAndManifest_ProducesSameRunId()
    {
        var tenantId = Guid.NewGuid();
        var manifestId = Guid.NewGuid();

        var first = OwnershipRunIdentity.Derive(tenantId, manifestId, null, NullLogger.Instance);
        var second = OwnershipRunIdentity.Derive(tenantId, manifestId, null, NullLogger.Instance);

        Assert.Equal(first, second);
    }

    [Fact]
    public void Derive_ExplicitRunId_AlwaysWins()
    {
        var explicitRunId = Guid.NewGuid();
        var result = OwnershipRunIdentity.Derive(Guid.NewGuid(), Guid.NewGuid(), explicitRunId, NullLogger.Instance);
        Assert.Equal(explicitRunId, result);
    }
}

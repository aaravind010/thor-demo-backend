using Microsoft.Extensions.Logging;

namespace Thor.Workflows.Ownership.Persistence;

/// <summary>Derives Ownership's RunId via the shared algorithm, using Ownership's own namespace GUID.</summary>
internal static class OwnershipRunIdentity
{
    /// <summary>Fixed namespace GUID for this module's deterministic run ids. Never change once shipped.</summary>
    private static readonly Guid Namespace = Guid.Parse("9c3f2b7a-1d4e-4a6b-8f2c-5e7d9a1b3c6f");

    public static Guid Derive(Guid tenantId, Guid? scanManifestId, Guid? explicitRunId, ILogger logger) =>
        Thor.Workflows.Hosting.DeterministicRunId.Derive(tenantId, scanManifestId, explicitRunId, Namespace, logger);
}

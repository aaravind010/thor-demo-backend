using Microsoft.Extensions.Logging;

namespace Thor.Workflows.Atre.Persistence;

/// <summary>Derives ATRE's RunId via the shared algorithm, using ATRE's own namespace GUID.</summary>
internal static class AtreRunIdentity
{
    /// <summary>Fixed namespace GUID for this module's deterministic run ids. Never change once shipped.</summary>
    private static readonly Guid Namespace = Guid.Parse("6f2a9e2e-6e2b-4b8a-9c9a-7b7a2f6b6a10");

    public static Guid Derive(Guid tenantId, Guid? scanManifestId, Guid? explicitRunId, ILogger logger) =>
        Thor.Workflows.Hosting.DeterministicRunId.Derive(tenantId, scanManifestId, explicitRunId, Namespace, logger);
}

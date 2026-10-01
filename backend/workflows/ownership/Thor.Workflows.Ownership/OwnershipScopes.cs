using Thor.Workflows.Ownership.Constants;

namespace Thor.Workflows.Ownership;

/// <summary>
/// Which <see cref="OwnershipAssignmentScope"/> a run matches under, and whether the request is one
/// a run can be started from at all. Resolved once, when the wave is planned, so every Map item
/// carries the concrete scope rather than re-deciding it.
/// </summary>
public static class OwnershipScopes
{
    private static readonly IReadOnlySet<string> Known = new HashSet<string>
    {
        OwnershipAssignmentScope.All,
        OwnershipAssignmentScope.UnassignedOnly,
        OwnershipAssignmentScope.UnassignedAndProposed,
        OwnershipAssignmentScope.UnassignedAndConfirmed,
    };

    /// <summary>
    /// The request's own scope when it names one. Otherwise a manifest-scoped run keeps the
    /// behaviour the scan-driven step has always had
    /// (<see cref="OwnershipAssignmentScope.UnassignedAndConfirmed"/>), and a tenant-wide run
    /// matches only what has no owner yet (<see cref="OwnershipAssignmentScope.UnassignedOnly"/>) —
    /// the defaults the two separate steps this record replaced each used.
    /// </summary>
    public static string Resolve(OwnershipRequest request) =>
        request.Scope
        ?? (request.ScanManifestId is not null
            ? OwnershipAssignmentScope.UnassignedAndConfirmed
            : OwnershipAssignmentScope.UnassignedOnly);

    /// <summary>
    /// Throws for a request no run should start from: an unknown scope, or a tenant-wide run with no
    /// run id. The second matters because a derived run id needs a manifest to derive from — without
    /// one, every step would mint its own random id and the steps of one run would never find each
    /// other's rows.
    /// </summary>
    public static void Validate(OwnershipRequest request)
    {
        var scope = Resolve(request);
        if (!Known.Contains(scope))
        {
            throw new ArgumentOutOfRangeException(nameof(request), scope, "Unknown Ownership assignment scope.");
        }

        if (request.ScanManifestId is null && request.RunId is null)
        {
            throw new InvalidOperationException("An Ownership run without a ScanManifestId requires an explicit RunId.");
        }
    }
}

namespace Thor.Graph;

/// <summary>
/// Edge labels the ingestion and ownership workflows write to Neptune. Readers accept only these,
/// so a caller-supplied relationship filter can never name an arbitrary label.
/// </summary>
public static class GraphRelTypes
{
    public const string MemberOf = "MEMBER_OF";

    public const string ReportsTo = "REPORTS_TO";

    public const string ManagedBy = "MANAGED_BY";

    public const string HasAccess = "HAS_ACCESS";

    public const string StoredIn = "STORED_IN";

    public const string OwnedBy = "OWNED_BY";

    public static readonly IReadOnlySet<string> All = new HashSet<string>
    {
        MemberOf, ReportsTo, ManagedBy, HasAccess, StoredIn, OwnedBy,
    };
}

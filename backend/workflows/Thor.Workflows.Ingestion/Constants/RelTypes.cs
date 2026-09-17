namespace Thor.Workflows.Ingestion.Constants;

/// <summary>
/// Which edge relationship types are structurally two-sided — stored on both endpoints (AD
/// emits <c>MEMBER_OF</c> from both <c>account.memberOf</c> and <c>grp.member</c>, §5.3;
/// Windows' local groups reuse the same <c>MEMBER_OF</c> convention from the group side only).
/// Everything else is one-sided and needs the edge gate's one-sided-reference healing (§7.3)
/// to resolve a late-arriving target.
/// </summary>
public static class RelTypes
{
    public static readonly IReadOnlySet<string> TwoSided = new HashSet<string> { "MEMBER_OF" };
}

namespace Thor.Auth;

/// <summary>
/// Format constants for the connector API-key credential (ADR §5.2):
/// <c>thor_{keyId}_{secret}</c>.
/// </summary>
public static class ApiKeyConstants
{
    public const string Prefix = "thor";
}

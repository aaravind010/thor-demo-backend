namespace Thor.DataLayer.Auth;

/// <summary>
/// Mints short-lived RDS IAM authentication tokens used as the password when opening a
/// PostgreSQL connection with IAM database authentication (see ADR §6.2). Tokens expire
/// (~15 min), so callers must generate one per physical connection open and never cache it.
/// Used by both the Master metadata DB factory and the per-tenant connection manager.
/// </summary>
public interface IRdsIamTokenProvider
{
    string GenerateToken(string host, int port, string dbUser, string region);
}

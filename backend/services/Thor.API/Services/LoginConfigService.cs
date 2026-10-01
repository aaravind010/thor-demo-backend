using Microsoft.EntityFrameworkCore;
using Thor.Api.Models;
using Thor.DataLayer.Data;
using Thor.DataLayer.Models;

namespace Thor.Api.Services;

/// <summary>
/// Backs <c>GET /login-config/{subdomain}</c>: tells the SPA which Cognito pool/app client to
/// sign a tenant's users in to, before any token exists. The subdomain is only a discovery hint
/// here — it grants nothing; authorization comes from the token the pool later issues, which the
/// Lambda authorizer maps back to the tenant by that pool (ADR §5).
/// </summary>
public sealed class LoginConfigService(IMasterDbContextFactory dbContextFactory, MasterConnectionInfo connectionInfo)
{
    /// <summary>Returns null for an unknown subdomain or a tenant that isn't active yet.</summary>
    public async Task<LoginConfigResponse?> GetAsync(string subdomain, CancellationToken cancellationToken)
    {
        await using var db = dbContextFactory.Create(connectionInfo);

        var routing = await db.Tenants
            .AsNoTracking()
            .Where(t => t.Subdomain == subdomain && t.StatusId == (short)TenantStatus.Active)
            .Select(t => t.Routing)
            .SingleOrDefaultAsync(cancellationToken);

        return routing is null
            ? null
            : new LoginConfigResponse(routing.UserPoolId, routing.AppClientId, routing.Region);
    }
}

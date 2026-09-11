using Microsoft.Extensions.Logging;
using Thor.TenantProvisioning.Core.Abstractions;
using Thor.TenantProvisioning.Core.Models;

namespace Thor.TenantProvisioning.Core.Steps;

/// <summary>
/// Step 4 — creates the tenant's DNS record for its subdomain. Idempotent (Route53
/// UPSERT). The subdomain label itself was already stored on the tenant row in step 1.
/// </summary>
public sealed class ConfigureSubdomainStep(
    ISubdomainProvisioner subdomain,
    ILogger<ConfigureSubdomainStep> logger)
{
    public async Task<ProvisioningState> RunAsync(ProvisioningState state, CancellationToken cancellationToken = default)
    {
        await subdomain.EnsureSubdomainAsync(state.Subdomain, cancellationToken);

        logger.LogInformation("Ensured DNS record for subdomain {Subdomain}.", state.Subdomain);
        return state;
    }
}

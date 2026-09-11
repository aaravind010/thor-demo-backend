using Microsoft.Extensions.Logging;
using Thor.TenantProvisioning.Core.Abstractions;
using Thor.TenantProvisioning.Core.Models;

namespace Thor.TenantProvisioning.Core.Steps;

/// <summary>
/// Step 2 — ensures the tenant's Cognito user pool + app client exist and the admin
/// group is present, returning the pool/client ids in the state. Idempotent via the
/// deterministic pool name (see <see cref="ProvisioningNames"/>).
/// </summary>
public sealed class ProvisionCognitoStep(
    ICognitoProvisioner cognito,
    ILogger<ProvisionCognitoStep> logger)
{
    public async Task<ProvisioningState> RunAsync(ProvisioningState state, CancellationToken cancellationToken = default)
    {
        var poolName = ProvisioningNames.UserPool(state.Subdomain);
        var appClientName = ProvisioningNames.AppClient(state.Subdomain);

        var pool = await cognito.EnsureUserPoolAsync(poolName, appClientName, cancellationToken);
        await cognito.EnsureAdminGroupAsync(pool.UserPoolId, ProvisioningNames.AdminGroup, cancellationToken);

        logger.LogInformation("Provisioned Cognito pool {UserPoolId} (client {AppClientId}) for subdomain {Subdomain}.",
            pool.UserPoolId, pool.AppClientId, state.Subdomain);
        return state with { UserPoolId = pool.UserPoolId, AppClientId = pool.AppClientId };
    }
}

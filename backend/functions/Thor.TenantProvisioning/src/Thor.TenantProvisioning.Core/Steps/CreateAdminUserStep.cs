using Microsoft.Extensions.Logging;
using Thor.TenantProvisioning.Core.Abstractions;
using Thor.TenantProvisioning.Core.Models;

namespace Thor.TenantProvisioning.Core.Steps;

/// <summary>
/// Step 3 — creates the tenant's local admin user in the Cognito pool and adds them to
/// the admin group, so the tenant can log in. Cognito issues the invite. Idempotent: a
/// re-run is a no-op if the user already exists.
/// </summary>
public sealed class CreateAdminUserStep(
    ICognitoProvisioner cognito,
    ILogger<CreateAdminUserStep> logger)
{
    public async Task<ProvisioningState> RunAsync(ProvisioningState state, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(state.UserPoolId))
        {
            throw new InvalidOperationException(
                "UserPoolId is not set; the ProvisionCognito step must run before CreateAdminUser.");
        }

        await cognito.EnsureAdminUserAsync(state.UserPoolId, ProvisioningNames.AdminGroup, state.AdminEmail, cancellationToken);

        logger.LogInformation("Ensured admin user {AdminEmail} in pool {UserPoolId}.", state.AdminEmail, state.UserPoolId);
        return state;
    }
}

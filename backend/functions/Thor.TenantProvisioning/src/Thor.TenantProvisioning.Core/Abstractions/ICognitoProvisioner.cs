namespace Thor.TenantProvisioning.Core.Abstractions;

/// <summary>
/// The Cognito operations tenant provisioning needs, kept AWS-SDK-agnostic so the step
/// logic stays testable. Every method must be idempotent: re-running a step (Step
/// Functions retry / DLQ redrive) must not create duplicates or fail on
/// already-existing resources.
/// </summary>
public interface ICognitoProvisioner
{
    /// <summary>
    /// Ensures a per-tenant user pool (ADR §5) and app client exist for the given
    /// deterministic names, returning their ids. Resolves an existing pool by name
    /// rather than creating a second one.
    /// </summary>
    Task<CognitoPool> EnsureUserPoolAsync(string poolName, string appClientName, CancellationToken cancellationToken = default);

    /// <summary>Ensures the named group exists in the pool (no-op if it already does).</summary>
    Task EnsureAdminGroupAsync(string userPoolId, string groupName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Ensures a local admin user exists in the pool and belongs to the group. Cognito
    /// sends the invite / temporary password; no credential is handled here.
    /// </summary>
    Task EnsureAdminUserAsync(string userPoolId, string groupName, string email, CancellationToken cancellationToken = default);
}

/// <summary>Identifiers for a provisioned Cognito user pool and its app client.</summary>
public sealed record CognitoPool(string UserPoolId, string AppClientId);

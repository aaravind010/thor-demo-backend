namespace Thor.TenantProvisioning.Core.Models;

/// <summary>
/// Values written to <c>tenant.status_id</c> during provisioning. The Master schema
/// stores status as a plain <c>smallint</c> (no lookup-table FK in the current
/// migration), so these codes are the convention the workflow relies on: a tenant is
/// <see cref="Provisioning"/> from the first step and only flips to <see cref="Active"/>
/// once routing and Cognito are fully set up (fail-closed, ADR §1).
/// </summary>
public enum TenantStatus : short
{
    Provisioning = 1,
    Active = 2,
}

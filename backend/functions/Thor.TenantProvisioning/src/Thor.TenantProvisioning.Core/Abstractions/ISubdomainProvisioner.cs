namespace Thor.TenantProvisioning.Core.Abstractions;

/// <summary>
/// Creates the tenant's DNS record. The base domain, hosted zone, and record target are
/// environment-level configuration resolved by the implementation; only the per-tenant
/// subdomain label flows through here. Must be idempotent (Route53 UPSERT).
/// </summary>
public interface ISubdomainProvisioner
{
    Task EnsureSubdomainAsync(string subdomain, CancellationToken cancellationToken = default);
}

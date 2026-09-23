namespace Thor.TenantProvisioning.Core.Models;

/// <summary>
/// Environment-level values written into every tenant's routing row. These are cluster
/// config, not per-tenant input. <see cref="ClusterEndpoint"/> is the endpoint the
/// runtime connects through — the RDS Proxy endpoint (ADR §6.3: access is always through
/// the proxy), not the raw Aurora writer used for provisioning DDL.
/// </summary>
public sealed record TenantRoutingOptions(string ClusterEndpoint, string Region);

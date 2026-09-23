namespace Thor.Core.Logging;

/// <summary>
/// Resolves the external HTTP log sink endpoint for a tenant, if one is configured.
/// Thor.Core has no knowledge of where this comes from (Master DB, config, etc.) — a
/// service opts into per-tenant sink routing by registering an implementation in DI.
/// Synchronous because it is called from Serilog.Sinks.Map's synchronous configure
/// callback; implementations must cache rather than block on every call.
/// </summary>
public interface ITenantLogSinkResolver
{
    TenantLogSinkOptions? Resolve(string tenantId);
}

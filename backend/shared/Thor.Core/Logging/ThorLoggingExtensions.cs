using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;

namespace Thor.Core.Logging;

/// <summary>
/// Bootstraps Serilog as the logging provider for a Thor service. Output format and
/// sinks (console, an external HTTP endpoint, etc.) are entirely caller-configured via
/// that service's own "Serilog" appsettings section (see Serilog.Settings.Configuration)
/// — this project does not hard-code format or sink choices.
/// </summary>
public static class ThorLoggingExtensions
{
    private const string TenantIdProperty = "TenantId";
    private const string NoTenantKey = "";

    // Serilog.Sinks.Http's own default when logEventsInBatchLimit is omitted entirely.
    // Passing null explicitly means "unbounded", a different behavior — so a tenant that
    // doesn't override BatchSizeLimit must still get this value, not null.
    private const int DefaultBatchSizeLimit = 1000;

    public static IHostBuilder UseThorLogging(this IHostBuilder hostBuilder) =>
        hostBuilder.UseSerilog((context, services, loggerConfiguration) =>
        {
            loggerConfiguration
                .ReadFrom.Configuration(context.Configuration)
                .ReadFrom.Services(services)
                .Enrich.FromLogContext();

            // Per-tenant HTTP sink routing is opt-in: only wired up if the service registers
            // an ITenantLogSinkResolver. Wrapped in WriteTo.Async so resolving the sink (a
            // Master DB lookup, cached) and writing to it happen on a background thread —
            // the request thread only ever enqueues an in-memory log event and returns.
            var tenantLogSinkResolver = services.GetService<ITenantLogSinkResolver>();
            if (tenantLogSinkResolver is not null)
            {
                loggerConfiguration.WriteTo.Async(sinkConfiguration => sinkConfiguration.Map(
                    keyPropertyName: TenantIdProperty,
                    defaultKey: NoTenantKey,
                    configure: (tenantId, writeTo) =>
                    {
                        var options = string.IsNullOrEmpty(tenantId) ? null : tenantLogSinkResolver.Resolve(tenantId);
                        if (options is not null)
                        {
                            writeTo.Http(
                                options.Endpoint,
                                queueLimitBytes: null,
                                logEventsInBatchLimit: options.BatchSizeLimit ?? DefaultBatchSizeLimit);
                        }
                    }));
            }
        });
}

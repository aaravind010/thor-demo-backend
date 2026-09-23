using Microsoft.AspNetCore.Http;
using Serilog.Context;

namespace Thor.Core.Logging;

/// <summary>
/// Attaches selected request headers to every log statement written during the request, by
/// pushing them onto Serilog's <see cref="LogContext"/>. Which headers matter, and what log
/// property each maps to, is entirely up to the calling service (see <paramref name="headerToPropertyMap"/>)
/// — this middleware has no built-in notion of tenancy, or of any other concept. Application
/// code logs normally; the mapped properties just show up as extra fields on whatever gets
/// logged, for debugging/filtering.
///
/// Values are forwarded as raw strings, unvalidated — this is a logging concern, not an
/// authorization one. Code that needs a parsed/validated value (e.g. a tenant id as a
/// <see cref="Guid"/>) does that itself, same as before this middleware existed.
///
/// Must be registered before any other logging middleware (e.g. <c>UseSerilogRequestLogging</c>)
/// so the pushed properties are still live when that middleware logs its completion line.
/// </summary>
public sealed class HeaderLogEnrichmentMiddleware(RequestDelegate next, IReadOnlyDictionary<string, string> headerToPropertyMap)
{
    public async Task InvokeAsync(HttpContext context)
    {
        var scopes = new Stack<IDisposable>(headerToPropertyMap.Count);
        try
        {
            foreach (var (headerName, propertyName) in headerToPropertyMap)
            {
                if (context.Request.Headers.TryGetValue(headerName, out var value) && !string.IsNullOrEmpty(value))
                {
                    scopes.Push(LogContext.PushProperty(propertyName, value.ToString()));
                }
            }

            await next(context);
        }
        finally
        {
            // Serilog's LogContext is itself a stack — pop in reverse push order.
            while (scopes.Count > 0)
            {
                scopes.Pop().Dispose();
            }
        }
    }
}

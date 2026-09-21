using Serilog.Context;

namespace Thor.Core.Logging;

/// <summary>
/// Lets a service attach ad-hoc properties to every log statement written during a scope,
/// without taking a direct dependency on Serilog — the service decides what belongs in the
/// context and when; this is a thin, disposable-returning wrapper over Serilog's LogContext.
/// </summary>
public static class ThorLogContext
{
    public static IDisposable PushProperty(string name, object? value) =>
        LogContext.PushProperty(name, value);

    /// <summary>
    /// Pushes multiple properties at once, returned as a single disposable that pops them
    /// all when disposed (in reverse push order, matching LogContext's own stack semantics).
    /// </summary>
    public static IDisposable PushProperties(IReadOnlyDictionary<string, object?> properties)
    {
        var scopes = new Stack<IDisposable>(properties.Count);
        foreach (var (name, value) in properties)
        {
            scopes.Push(LogContext.PushProperty(name, value));
        }

        return new CompositeScope(scopes);
    }

    private sealed class CompositeScope(Stack<IDisposable> scopes) : IDisposable
    {
        public void Dispose()
        {
            while (scopes.Count > 0)
            {
                scopes.Pop().Dispose();
            }
        }
    }
}

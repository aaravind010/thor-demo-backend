using System.Text;
using Gremlin.Net.Driver;
using Gremlin.Net.Driver.Messages;
using Gremlin.Net.Process.Traversal;

namespace Thor.Graph.Test.Fakes;

/// <summary>
/// Stands in for Neptune: records each submitted request and answers with the next queued result
/// set (empty once the queue runs out). Thor.Graph's only network dependency is this interface, so a
/// hand-rolled fake covers it without a mocking framework.
/// </summary>
internal sealed class FakeGremlinClient : IGremlinClient
{
    private readonly Queue<IReadOnlyList<object>> _results = new();

    public List<RequestMessage> Requests { get; } = [];

    /// <summary>Queues the rows the next traversal returns.</summary>
    public FakeGremlinClient Returns(params object[] rows)
    {
        _results.Enqueue(rows);
        return this;
    }

    public Task<ResultSet<T>> SubmitAsync<T>(RequestMessage requestMessage, CancellationToken cancellationToken = default)
    {
        Requests.Add(requestMessage);
        var rows = _results.TryDequeue(out var queued) ? queued : [];
        // DriverRemoteConnection asks for Traverser results; wrap each row as one.
        var traversers = rows.Select(r => (T)(object)new Traverser(r)).ToList();
        return Task.FromResult(new ResultSet<T>(traversers, new Dictionary<string, object>()));
    }

    public void Dispose()
    {
    }
}

internal static class RequestMessageExtensions
{
    public static Bytecode Bytecode(this RequestMessage message) => (Bytecode)message.Arguments["gremlin"];

    /// <summary>
    /// Renders bytecode as Gremlin-ish text (<c>V(x).has(tenantId,y)...</c>), recursing into nested
    /// anonymous traversals, so tests can assert on steps without depending on the driver's own format.
    /// </summary>
    public static string Text(this RequestMessage message) => Render(message.Bytecode());

    private static string Render(Bytecode bytecode)
    {
        var sb = new StringBuilder();
        foreach (var instruction in bytecode.StepInstructions)
        {
            if (sb.Length > 0)
            {
                sb.Append('.');
            }
            sb.Append(instruction.OperatorName).Append('(')
                .Append(string.Join(",", instruction.Arguments.Select(RenderArgument)))
                .Append(')');
        }
        return sb.ToString();
    }

    private static string RenderArgument(object? argument) => argument switch
    {
        Bytecode nested => Render(nested),
        ITraversal traversal => Render(traversal.Bytecode),
        P p => $"{p.OperatorName}({RenderArgument(p.Value)})",
        object[] values => string.Join(",", values.Select(RenderArgument)),
        System.Collections.IEnumerable values and not string => string.Join(",", values.Cast<object?>().Select(RenderArgument)),
        _ => Convert.ToString(argument) ?? "null",
    };
}

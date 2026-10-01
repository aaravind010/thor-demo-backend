using Gremlin.Net.Driver;
using Gremlin.Net.Driver.Messages;

namespace Thor.Workflows.Ingestion.Tests.Graph;

/// <summary>
/// Captures every submitted <see cref="RequestMessage"/> instead of talking to a real Neptune
/// cluster — Thor.Graph's only network dependency is this one interface, so a hand-rolled fake
/// covers it without pulling in a mocking framework the repo doesn't otherwise use.
/// </summary>
internal sealed class FakeGremlinClient : IGremlinClient
{
    public List<RequestMessage> Requests { get; } = [];

    public Task<ResultSet<T>> SubmitAsync<T>(RequestMessage requestMessage, CancellationToken cancellationToken = default)
    {
        Requests.Add(requestMessage);
        return Task.FromResult<ResultSet<T>>(null!);
    }

    public void Dispose()
    {
    }
}

internal static class RequestMessageExtensions
{
    public static string Script(this RequestMessage message) => (string)message.Arguments["gremlin"];

    public static IReadOnlyDictionary<string, object> Bindings(this RequestMessage message) =>
        (Dictionary<string, object>)message.Arguments["bindings"];
}

namespace Thor.Api.Services;

public interface IIntelligenceEngineClient
{
    Task<string> InvokeAgentAsync(string tenantId, string agentName, string input, CancellationToken cancellationToken = default);
}

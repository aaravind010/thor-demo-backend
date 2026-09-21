using Thor.IntelligenceEngine.Grpc.V1;

namespace Thor.Api.Services;

// Thin wrapper around the generated gRPC client stub. tenant_id is passed as a plain
// field, not a signed token - the Intelligence Engine is only reachable from Thor.Api
// over the private network (ADR §4), so this is treated like a library call, trusting
// network isolation rather than an app-layer credential.
public class IntelligenceEngineClient(IntelligenceEngineService.IntelligenceEngineServiceClient client) : IIntelligenceEngineClient
{
    public async Task<string> InvokeAgentAsync(string tenantId, string agentName, string input, CancellationToken cancellationToken = default)
    {
        var request = new InvokeAgentRequest
        {
            TenantId = tenantId,
            AgentName = agentName,
            Input = input,
        };

        var response = await client.InvokeAgentAsync(request, cancellationToken: cancellationToken);
        return response.Output;
    }
}

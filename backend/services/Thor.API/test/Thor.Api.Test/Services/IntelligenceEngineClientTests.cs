using System.Net;
using FluentAssertions;
using Grpc.Core;
using Grpc.Net.Client;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.DependencyInjection;
using Thor.Api.Services;
using Thor.IntelligenceEngine.Grpc.V1;

namespace Thor.Api.Test.Services;

/// <summary>
/// Exercises IntelligenceEngineClient against a real in-process gRPC server (a fake service
/// implementation of the generated base class), matching this repo's preference for real
/// implementations over mocks.
/// </summary>
public class IntelligenceEngineClientTests : IAsyncLifetime
{
    private WebApplication _server = null!;
    private GrpcChannel _channel = null!;

    public async Task InitializeAsync()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseKestrel(o => o.Listen(IPAddress.Loopback, 0, lo => lo.Protocols = HttpProtocols.Http2));
        builder.Services.AddGrpc();

        _server = builder.Build();
        _server.MapGrpcService<FakeIntelligenceEngineService>();
        await _server.StartAsync();

        _channel = GrpcChannel.ForAddress(_server.Urls.First());
    }

    public async Task DisposeAsync()
    {
        await _channel.ShutdownAsync();
        await _server.StopAsync();
    }

    [Fact]
    public async Task InvokeAgentAsync_ReturnsServerResponse()
    {
        var client = new IntelligenceEngineClient(new IntelligenceEngineService.IntelligenceEngineServiceClient(_channel));

        var result = await client.InvokeAgentAsync("tenant-1", "echo", "hello");

        result.Should().Be("echo:hello");
    }

    private class FakeIntelligenceEngineService : IntelligenceEngineService.IntelligenceEngineServiceBase
    {
        public override Task<InvokeAgentResponse> InvokeAgent(InvokeAgentRequest request, ServerCallContext context) =>
            Task.FromResult(new InvokeAgentResponse { Output = $"{request.AgentName}:{request.Input}" });
    }
}

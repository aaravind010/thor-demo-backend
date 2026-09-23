using Amazon.Lambda.Core;
using Microsoft.Extensions.DependencyInjection;
using Thor.TenantProvisioning.Core.Models;
using Thor.TenantProvisioning.Core.Steps;

namespace Thor.TenantProvisioning.Function.Handlers;

/// <summary>Step Functions task 5 (final) — see <see cref="FinalizeRoutingStep"/>.</summary>
public sealed class FinalizeRoutingFunction
{
    private readonly IServiceProvider _services;

    public FinalizeRoutingFunction() : this(CompositionRoot.BuildServiceProvider())
    {
    }

    internal FinalizeRoutingFunction(IServiceProvider services) => _services = services;

    public async Task<ProvisioningState> Handle(ProvisioningState state, ILambdaContext context)
    {
        using var scope = _services.CreateScope();
        var step = scope.ServiceProvider.GetRequiredService<FinalizeRoutingStep>();
        return await step.RunAsync(state);
    }
}

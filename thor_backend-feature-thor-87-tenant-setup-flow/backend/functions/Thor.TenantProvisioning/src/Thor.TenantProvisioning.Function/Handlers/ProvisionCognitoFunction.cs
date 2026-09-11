using Amazon.Lambda.Core;
using Microsoft.Extensions.DependencyInjection;
using Thor.TenantProvisioning.Core.Models;
using Thor.TenantProvisioning.Core.Steps;

namespace Thor.TenantProvisioning.Function.Handlers;

/// <summary>Step Functions task 2 — see <see cref="ProvisionCognitoStep"/>.</summary>
public sealed class ProvisionCognitoFunction
{
    private readonly IServiceProvider _services;

    public ProvisionCognitoFunction() : this(CompositionRoot.BuildServiceProvider())
    {
    }

    internal ProvisionCognitoFunction(IServiceProvider services) => _services = services;

    public async Task<ProvisioningState> Handle(ProvisioningState state, ILambdaContext context)
    {
        using var scope = _services.CreateScope();
        var step = scope.ServiceProvider.GetRequiredService<ProvisionCognitoStep>();
        return await step.RunAsync(state);
    }
}

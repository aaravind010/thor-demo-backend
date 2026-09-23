using Amazon.Lambda.Core;
using Microsoft.Extensions.DependencyInjection;
using Thor.TenantProvisioning.Core.Models;
using Thor.TenantProvisioning.Core.Steps;

namespace Thor.TenantProvisioning.Function.Handlers;

/// <summary>Step Functions task 4 — see <see cref="ConfigureSubdomainStep"/>.</summary>
public sealed class ConfigureSubdomainFunction
{
    private readonly IServiceProvider _services;

    public ConfigureSubdomainFunction() : this(CompositionRoot.BuildServiceProvider())
    {
    }

    internal ConfigureSubdomainFunction(IServiceProvider services) => _services = services;

    public async Task<ProvisioningState> Handle(ProvisioningState state, ILambdaContext context)
    {
        using var scope = _services.CreateScope();
        var step = scope.ServiceProvider.GetRequiredService<ConfigureSubdomainStep>();
        return await step.RunAsync(state);
    }
}

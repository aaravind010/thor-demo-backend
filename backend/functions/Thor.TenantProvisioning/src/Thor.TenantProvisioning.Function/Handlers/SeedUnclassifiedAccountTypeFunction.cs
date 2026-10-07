using Amazon.Lambda.Core;
using Microsoft.Extensions.DependencyInjection;
using Thor.TenantProvisioning.Core.Models;
using Thor.TenantProvisioning.Core.Steps;

namespace Thor.TenantProvisioning.Function.Handlers;

/// <summary>Step Functions task that runs after routing is finalized — see <see cref="SeedUnclassifiedAccountTypeStep"/>.</summary>
public sealed class SeedUnclassifiedAccountTypeFunction
{
    private readonly IServiceProvider _services;

    public SeedUnclassifiedAccountTypeFunction() : this(CompositionRoot.BuildServiceProvider())
    {
    }

    internal SeedUnclassifiedAccountTypeFunction(IServiceProvider services) => _services = services;

    public async Task<ProvisioningState> Handle(ProvisioningState state, ILambdaContext context)
    {
        using var scope = _services.CreateScope();
        var step = scope.ServiceProvider.GetRequiredService<SeedUnclassifiedAccountTypeStep>();
        return await step.RunAsync(state);
    }
}

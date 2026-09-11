using Amazon.Lambda.Core;
using Microsoft.Extensions.DependencyInjection;
using Thor.TenantProvisioning.Core.Models;
using Thor.TenantProvisioning.Core.Steps;

namespace Thor.TenantProvisioning.Function.Handlers;

/// <summary>Step Functions task 1 — see <see cref="SeedTenantMetadataStep"/>.</summary>
public sealed class SeedTenantMetadataFunction
{
    private readonly IServiceProvider _services;

    public SeedTenantMetadataFunction() : this(CompositionRoot.BuildServiceProvider())
    {
    }

    internal SeedTenantMetadataFunction(IServiceProvider services) => _services = services;

    public async Task<ProvisioningState> Handle(ProvisioningState state, ILambdaContext context)
    {
        using var scope = _services.CreateScope();
        var step = scope.ServiceProvider.GetRequiredService<SeedTenantMetadataStep>();
        return await step.RunAsync(state);
    }
}

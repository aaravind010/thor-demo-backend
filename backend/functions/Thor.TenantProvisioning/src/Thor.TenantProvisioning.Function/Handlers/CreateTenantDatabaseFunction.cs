using Amazon.Lambda.Core;
using Microsoft.Extensions.DependencyInjection;
using Thor.TenantProvisioning.Core.Models;
using Thor.TenantProvisioning.Core.Steps;

namespace Thor.TenantProvisioning.Function.Handlers;

/// <summary>Step Functions task 2 — see <see cref="CreateTenantDatabaseStep"/>.</summary>
public sealed class CreateTenantDatabaseFunction
{
    private readonly IServiceProvider _services;

    public CreateTenantDatabaseFunction() : this(CompositionRoot.BuildServiceProvider())
    {
    }

    internal CreateTenantDatabaseFunction(IServiceProvider services) => _services = services;

    public async Task<ProvisioningState> Handle(ProvisioningState state, ILambdaContext context)
    {
        using var scope = _services.CreateScope();
        var step = scope.ServiceProvider.GetRequiredService<CreateTenantDatabaseStep>();
        return await step.RunAsync(state);
    }
}

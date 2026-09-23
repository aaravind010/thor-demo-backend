using Amazon.Lambda.Core;
using Microsoft.Extensions.DependencyInjection;
using Thor.TenantProvisioning.Core.Models;
using Thor.TenantProvisioning.Core.Steps;

namespace Thor.TenantProvisioning.Function.Handlers;

/// <summary>Step Functions task 3 — see <see cref="CreateTenantTablesStep"/>.</summary>
public sealed class CreateTenantTablesFunction
{
    private readonly IServiceProvider _services;

    public CreateTenantTablesFunction() : this(CompositionRoot.BuildServiceProvider())
    {
    }

    internal CreateTenantTablesFunction(IServiceProvider services) => _services = services;

    public async Task<ProvisioningState> Handle(ProvisioningState state, ILambdaContext context)
    {
        using var scope = _services.CreateScope();
        var step = scope.ServiceProvider.GetRequiredService<CreateTenantTablesStep>();
        return await step.RunAsync(state);
    }
}

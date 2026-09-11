using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Thor.TenantProvisioning.Core.Abstractions;
using Thor.TenantProvisioning.Core.Steps;

namespace Thor.TenantProvisioning.Test.Steps;

public sealed class ConfigureSubdomainStepTests
{
    [Fact]
    public async Task Ensures_dns_record_for_the_tenant_subdomain()
    {
        var subdomain = Substitute.For<ISubdomainProvisioner>();
        var step = new ConfigureSubdomainStep(subdomain, NullLogger<ConfigureSubdomainStep>.Instance);

        await step.RunAsync(TestState.Sample());

        await subdomain.Received(1).EnsureSubdomainAsync("acme", Arg.Any<CancellationToken>());
    }
}

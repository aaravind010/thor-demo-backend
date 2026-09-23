using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Thor.TenantProvisioning.Core;
using Thor.TenantProvisioning.Core.Abstractions;
using Thor.TenantProvisioning.Core.Steps;

namespace Thor.TenantProvisioning.Test.Steps;

public sealed class CognitoStepsTests
{
    private readonly ICognitoProvisioner _cognito = Substitute.For<ICognitoProvisioner>();

    [Fact]
    public async Task ProvisionCognito_creates_pool_and_group_and_records_ids()
    {
        _cognito.EnsureUserPoolAsync("thor-acme", "thor-acme-app", Arg.Any<CancellationToken>())
            .Returns(new CognitoPool("pool-123", "client-456"));

        var step = new ProvisionCognitoStep(_cognito, NullLogger<ProvisionCognitoStep>.Instance);
        var result = await step.RunAsync(TestState.Sample());

        result.UserPoolId.Should().Be("pool-123");
        result.AppClientId.Should().Be("client-456");
        await _cognito.Received(1).EnsureAdminGroupAsync("pool-123", ProvisioningNames.AdminGroup, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CreateAdminUser_throws_when_pool_not_yet_provisioned()
    {
        var step = new CreateAdminUserStep(_cognito, NullLogger<CreateAdminUserStep>.Instance);

        var act = () => step.RunAsync(TestState.Sample());

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task CreateAdminUser_creates_user_when_pool_present()
    {
        var step = new CreateAdminUserStep(_cognito, NullLogger<CreateAdminUserStep>.Instance);
        var state = TestState.Sample() with { UserPoolId = "pool-123" };

        await step.RunAsync(state);

        await _cognito.Received(1).EnsureAdminUserAsync(
            "pool-123", ProvisioningNames.AdminGroup, "admin@acme.example.com", Arg.Any<CancellationToken>());
    }
}

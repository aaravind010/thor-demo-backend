using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Thor.TenantProvisioning.Core.Abstractions;
using Thor.TenantProvisioning.Core.Steps;

namespace Thor.TenantProvisioning.Test.Steps;

public sealed class CreateTenantDatabaseStepTests
{
    private readonly ITenantDatabaseProvisioner _provisioner = Substitute.For<ITenantDatabaseProvisioner>();

    private CreateTenantDatabaseStep CreateStep() =>
        new(_provisioner, NullLogger<CreateTenantDatabaseStep>.Instance);

    [Fact]
    public async Task Throws_when_tenant_id_missing()
    {
        var act = () => CreateStep().RunAsync(TestState.Sample());
        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task Provisions_database_and_records_names_and_db_users()
    {
        var tenantId = Guid.NewGuid();
        _provisioner.ProvisionAsync(Arg.Any<TenantDatabaseRequest>(), Arg.Any<CancellationToken>())
            .Returns(new TenantDatabaseResult("tenant_acme", "tenant_acme_rw", "tenant_acme_ro"));

        var result = await CreateStep().RunAsync(TestState.Sample() with { TenantId = tenantId });

        result.DatabaseName.Should().Be("tenant_acme");
        result.RwDbUser.Should().Be("tenant_acme_rw");
        result.RoDbUser.Should().Be("tenant_acme_ro");
        await _provisioner.Received(1).ProvisionAsync(
            Arg.Is<TenantDatabaseRequest>(r => r != null && r.TenantId == tenantId && r.Subdomain == "acme"),
            Arg.Any<CancellationToken>());
    }
}

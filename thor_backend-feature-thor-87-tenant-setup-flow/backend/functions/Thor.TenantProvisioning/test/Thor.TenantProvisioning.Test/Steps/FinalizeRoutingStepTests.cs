using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Thor.DataLayer.Models;
using Thor.DataLayer.Repositories;
using Thor.TenantProvisioning.Core.Models;
using Thor.TenantProvisioning.Core.Steps;

namespace Thor.TenantProvisioning.Test.Steps;

public sealed class FinalizeRoutingStepTests
{
    private readonly ITenantRepository _tenants = Substitute.For<ITenantRepository>();
    private readonly ITenantRoutingRepository _routings = Substitute.For<ITenantRoutingRepository>();
    private readonly TenantRoutingOptions _routingOptions = new("proxy.example.aws", "us-east-1");
    private readonly FakeTimeProvider _time = new();

    private FinalizeRoutingStep CreateStep() =>
        new(_tenants, _routings, _routingOptions, _time, NullLogger<FinalizeRoutingStep>.Instance);

    private static ProvisioningState ReadyState(Guid tenantId) => TestState.Sample() with
    {
        TenantId = tenantId,
        DatabaseName = "tenant_acme",
        RwDbUser = "tenant_acme_rw",
        RoDbUser = "tenant_acme_ro",
        UserPoolId = "pool-123",
        AppClientId = "client-456",
    };

    [Fact]
    public async Task Throws_when_tenant_id_missing()
    {
        var act = () => CreateStep().RunAsync(ReadyState(Guid.NewGuid()) with { TenantId = null });
        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task Throws_when_database_routing_missing()
    {
        var act = () => CreateStep().RunAsync(ReadyState(Guid.NewGuid()) with { RwDbUser = null });
        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task Throws_when_cognito_ids_missing()
    {
        var act = () => CreateStep().RunAsync(ReadyState(Guid.NewGuid()) with { UserPoolId = null });
        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task Inserts_routing_with_both_db_users_and_activates_tenant()
    {
        var tenantId = Guid.NewGuid();
        var tenant = new Tenant { TenantId = tenantId, Subdomain = "acme", DisplayName = "Acme" };
        _routings.GetByIdAsync(tenantId, Arg.Any<CancellationToken>()).Returns((TenantRouting?)null);
        _tenants.GetByIdAsync(tenantId, Arg.Any<CancellationToken>()).Returns(tenant);
        TenantRouting? captured = null;
        await _routings.AddAsync(Arg.Do<TenantRouting>(r => captured = r), Arg.Any<CancellationToken>());

        await CreateStep().RunAsync(ReadyState(tenantId));

        captured.Should().NotBeNull();
        captured!.ClusterEndpoint.Should().Be("proxy.example.aws");
        captured.Region.Should().Be("us-east-1");
        captured.DatabaseName.Should().Be("tenant_acme");
        captured.DbUser.Should().Be("tenant_acme_rw");
        captured.ReadOnlyDbUser.Should().Be("tenant_acme_ro");
        captured.UserPoolId.Should().Be("pool-123");
        tenant.StatusId.Should().Be((short)TenantStatus.Active);
        await _routings.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Updates_routing_in_place_when_already_present()
    {
        var tenantId = Guid.NewGuid();
        var tenant = new Tenant { TenantId = tenantId, Subdomain = "acme", DisplayName = "Acme" };
        var existingRouting = new TenantRouting { TenantId = tenantId, DbUser = "old", ReadOnlyDbUser = "old" };
        _routings.GetByIdAsync(tenantId, Arg.Any<CancellationToken>()).Returns(existingRouting);
        _tenants.GetByIdAsync(tenantId, Arg.Any<CancellationToken>()).Returns(tenant);

        await CreateStep().RunAsync(ReadyState(tenantId));

        existingRouting.DbUser.Should().Be("tenant_acme_rw");
        existingRouting.ReadOnlyDbUser.Should().Be("tenant_acme_ro");
        await _routings.DidNotReceive().AddAsync(Arg.Any<TenantRouting>(), Arg.Any<CancellationToken>());
        _routings.Received(1).Update(existingRouting);
        tenant.StatusId.Should().Be((short)TenantStatus.Active);
    }
}

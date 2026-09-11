using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Thor.DataLayer.Models;
using Thor.DataLayer.Repositories;
using Thor.TenantProvisioning.Core.Models;
using Thor.TenantProvisioning.Core.Steps;

namespace Thor.TenantProvisioning.Test.Steps;

public sealed class SeedTenantMetadataStepTests
{
    private readonly ITenantRepository _tenants = Substitute.For<ITenantRepository>();
    private readonly FakeTimeProvider _time = new();

    private SeedTenantMetadataStep CreateStep() =>
        new(_tenants, _time, NullLogger<SeedTenantMetadataStep>.Instance);

    [Fact]
    public async Task Inserts_new_tenant_when_subdomain_is_free()
    {
        _tenants.GetBySubdomainAsync("acme", Arg.Any<CancellationToken>()).Returns((Tenant?)null);
        Tenant? captured = null;
        await _tenants.AddAsync(Arg.Do<Tenant>(t => captured = t), Arg.Any<CancellationToken>());

        var result = await CreateStep().RunAsync(TestState.Sample());

        captured.Should().NotBeNull();
        captured!.Subdomain.Should().Be("acme");
        captured.StatusId.Should().Be((short)TenantStatus.Provisioning);
        result.TenantId.Should().Be(captured.TenantId);
        await _tenants.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Reuses_existing_tenant_and_does_not_insert()
    {
        var existing = new Tenant { TenantId = Guid.NewGuid(), Subdomain = "acme", DisplayName = "Acme" };
        _tenants.GetBySubdomainAsync("acme", Arg.Any<CancellationToken>()).Returns(existing);

        var result = await CreateStep().RunAsync(TestState.Sample());

        result.TenantId.Should().Be(existing.TenantId);
        await _tenants.DidNotReceive().AddAsync(Arg.Any<Tenant>(), Arg.Any<CancellationToken>());
        await _tenants.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }
}

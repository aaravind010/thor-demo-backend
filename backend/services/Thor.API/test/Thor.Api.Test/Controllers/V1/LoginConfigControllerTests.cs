using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Thor.Api.Controllers.V1;
using Thor.Api.Models;
using Thor.Api.Services;
using Thor.Api.Test.TestFixtures;
using Thor.DataLayer.Data;
using Thor.DataLayer.Models;

namespace Thor.Api.Test.Controllers.V1;

/// <summary>
/// Exercises LoginConfigController wired to a real LoginConfigService over an EF Core InMemory
/// Master DB: only an active tenant with routing yields its pool/client ids.
/// </summary>
public class LoginConfigControllerTests
{
    private static readonly MasterConnectionInfo ConnectionInfo = new("localhost", "unused", "unused", "unused");

    private static (LoginConfigController Controller, InMemoryMasterDbContextFactory Factory) CreateController()
    {
        var factory = new InMemoryMasterDbContextFactory(Guid.NewGuid().ToString());
        return (new LoginConfigController(new LoginConfigService(factory, ConnectionInfo)), factory);
    }

    internal static void SeedTenant(InMemoryMasterDbContextFactory factory, string subdomain, TenantStatus status, bool withRouting = true)
    {
        var tenantId = Guid.NewGuid();
        using var db = factory.Create(ConnectionInfo);
        db.Tenants.Add(new Tenant
        {
            TenantId = tenantId,
            DisplayName = subdomain,
            Subdomain = subdomain,
            StatusId = (short)status,
            Routing = withRouting
                ? new TenantRouting
                {
                    TenantId = tenantId,
                    ClusterEndpoint = "tenant-proxy.local",
                    DatabaseName = $"tenant_{subdomain}",
                    Region = "us-east-1",
                    UserPoolId = $"us-east-1_{subdomain}Pool",
                    AppClientId = $"{subdomain}-client",
                    DbUser = $"tenant_{subdomain}_rw",
                    ReadOnlyDbUser = $"tenant_{subdomain}_ro",
                }
                : null,
        });
        db.SaveChanges();
    }

    [Fact]
    public async Task Get_ActiveTenant_ReturnsItsPoolClientAndRegion()
    {
        var (controller, factory) = CreateController();
        SeedTenant(factory, "acme", TenantStatus.Active);
        SeedTenant(factory, "other", TenantStatus.Active);

        var result = await controller.Get("acme", CancellationToken.None);

        var ok = result.Result.Should().BeOfType<OkObjectResult>().Subject;
        ok.Value.Should().Be(new LoginConfigResponse("us-east-1_acmePool", "acme-client", "us-east-1"));
    }

    [Fact]
    public async Task Get_UnknownSubdomain_ReturnsNotFound()
    {
        var (controller, factory) = CreateController();
        SeedTenant(factory, "acme", TenantStatus.Active);

        var result = await controller.Get("missing", CancellationToken.None);

        result.Result.Should().BeOfType<NotFoundResult>();
    }

    [Fact]
    public async Task Get_TenantStillProvisioning_ReturnsNotFound()
    {
        var (controller, factory) = CreateController();
        SeedTenant(factory, "acme", TenantStatus.Provisioning);

        var result = await controller.Get("acme", CancellationToken.None);

        result.Result.Should().BeOfType<NotFoundResult>();
    }

    [Fact]
    public async Task Get_ActiveTenantWithoutRouting_ReturnsNotFound()
    {
        var (controller, factory) = CreateController();
        SeedTenant(factory, "acme", TenantStatus.Active, withRouting: false);

        var result = await controller.Get("acme", CancellationToken.None);

        result.Result.Should().BeOfType<NotFoundResult>();
    }
}

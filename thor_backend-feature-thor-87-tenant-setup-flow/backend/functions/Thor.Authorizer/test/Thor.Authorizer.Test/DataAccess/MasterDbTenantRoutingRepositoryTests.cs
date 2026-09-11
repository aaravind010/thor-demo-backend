using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Thor.Authorizer.Function.DataAccess;
using Thor.DataLayer.Data;
using Thor.DataLayer.Models;

namespace Thor.Authorizer.Test.DataAccess;

public class MasterDbTenantRoutingRepositoryTests
{
    private static readonly MasterConnectionInfo ConnectionInfo =
        new(Host: "proxy.local", Database: "thor_master", Username: "thor_authorizer", Region: "us-east-1");

    [Fact]
    public async Task GetBySubdomainAsync_MapsTenantAndRouting_ToTenantRoute()
    {
        var databaseName = Guid.NewGuid().ToString();
        var tenantId = Guid.Parse("11111111-1111-1111-1111-111111111111");

        await SeedAsync(databaseName, new Tenant
        {
            TenantId = tenantId,
            DisplayName = "Acme",
            Subdomain = "acme",
            Routing = new TenantRouting
            {
                TenantId = tenantId,
                ClusterEndpoint = "tenant-proxy.local",
                DatabaseName = "tenant_acme",
                Region = "us-east-1",
                UserPoolId = "pool-1",
                AppClientId = "client-1",
                DbUser = "tenant_acme_rw",
                ReadOnlyDbUser = "tenant_acme_ro",
            },
        });

        var route = await new MasterDbTenantRoutingRepository(new InMemoryFactory(databaseName), ConnectionInfo)
            .GetBySubdomainAsync("acme");

        route.Should().NotBeNull();
        route!.TenantId.Should().Be(tenantId.ToString());
        route.Subdomain.Should().Be("acme");
        route.UserPoolId.Should().Be("pool-1");
        route.AppClientId.Should().Be("client-1");
        route.Region.Should().Be("us-east-1");
    }

    [Fact]
    public async Task GetBySubdomainAsync_ReturnsNull_WhenSubdomainUnknown()
    {
        var databaseName = Guid.NewGuid().ToString();

        var route = await new MasterDbTenantRoutingRepository(new InMemoryFactory(databaseName), ConnectionInfo)
            .GetBySubdomainAsync("missing");

        route.Should().BeNull();
    }

    private static async Task SeedAsync(string databaseName, Tenant tenant)
    {
        await using var context = new MasterDbContext(OptionsFor(databaseName));
        context.Tenants.Add(tenant);
        await context.SaveChangesAsync();
    }

    private static DbContextOptions<MasterDbContext> OptionsFor(string databaseName) =>
        new DbContextOptionsBuilder<MasterDbContext>()
            .UseInMemoryDatabase(databaseName)
            .Options;

    // Stand-in for MasterDbContextFactory: the real factory opens an Npgsql connection with an
    // RDS IAM token, so tests bind the same MasterDbContext to the EF Core in-memory provider,
    // keyed to a per-test database so seeded data is visible to the repository's own context.
    private sealed class InMemoryFactory(string databaseName) : IMasterDbContextFactory
    {
        public MasterDbContext Create(MasterConnectionInfo connectionInfo) =>
            new(OptionsFor(databaseName));
    }
}

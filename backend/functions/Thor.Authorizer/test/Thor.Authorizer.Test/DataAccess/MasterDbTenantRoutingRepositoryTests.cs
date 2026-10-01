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

    private static readonly Guid TenantId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    [Fact]
    public async Task GetByUserPoolIdAsync_MapsRouting_ToTenantRoute()
    {
        var databaseName = await SeedAcmeAsync();

        var route = await CreateRepository(databaseName).GetByUserPoolIdAsync("us-east-1_Pool1");

        route.Should().NotBeNull();
        route!.TenantId.Should().Be(TenantId.ToString());
        route.UserPoolId.Should().Be("us-east-1_Pool1");
        route.AppClientId.Should().Be("client-1");
        route.Region.Should().Be("us-east-1");
    }

    [Fact]
    public async Task GetByUserPoolIdAsync_ReturnsNull_WhenPoolUnknown()
    {
        var databaseName = await SeedAcmeAsync();

        var route = await CreateRepository(databaseName).GetByUserPoolIdAsync("us-east-1_Missing");

        route.Should().BeNull();
    }

    [Fact]
    public async Task GetByUserPoolIdAsync_IsCaseSensitive()
    {
        var databaseName = await SeedAcmeAsync();

        var route = await CreateRepository(databaseName).GetByUserPoolIdAsync("us-east-1_pool1");

        route.Should().BeNull();
    }

    [Fact]
    public async Task GetByTenantIdAsync_MapsRouting_ToTenantRoute()
    {
        var databaseName = await SeedAcmeAsync();

        var route = await CreateRepository(databaseName).GetByTenantIdAsync(TenantId.ToString());

        route.Should().NotBeNull();
        route!.TenantId.Should().Be(TenantId.ToString());
        route.UserPoolId.Should().Be("us-east-1_Pool1");
    }

    [Theory]
    [InlineData("22222222-2222-2222-2222-222222222222")]
    [InlineData("not-a-guid")]
    public async Task GetByTenantIdAsync_ReturnsNull_WhenTenantUnknownOrMalformed(string tenantId)
    {
        var databaseName = await SeedAcmeAsync();

        var route = await CreateRepository(databaseName).GetByTenantIdAsync(tenantId);

        route.Should().BeNull();
    }

    private static MasterDbTenantRoutingRepository CreateRepository(string databaseName) =>
        new(new InMemoryFactory(databaseName), ConnectionInfo);

    private static async Task<string> SeedAcmeAsync()
    {
        var databaseName = Guid.NewGuid().ToString();

        await using var context = new MasterDbContext(OptionsFor(databaseName));
        context.Tenants.Add(new Tenant
        {
            TenantId = TenantId,
            DisplayName = "Acme",
            Subdomain = "acme",
            Routing = new TenantRouting
            {
                TenantId = TenantId,
                ClusterEndpoint = "tenant-proxy.local",
                DatabaseName = "tenant_acme",
                Region = "us-east-1",
                UserPoolId = "us-east-1_Pool1",
                AppClientId = "client-1",
                DbUser = "tenant_acme_rw",
                ReadOnlyDbUser = "tenant_acme_ro",
            },
        });
        await context.SaveChangesAsync();

        return databaseName;
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

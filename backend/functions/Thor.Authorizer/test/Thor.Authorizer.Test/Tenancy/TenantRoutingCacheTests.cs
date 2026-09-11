using FluentAssertions;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Thor.Authorizer.Core.DataAccess;
using Thor.Authorizer.Core.Tenancy;
using Thor.Authorizer.Test.TestFixtures;

namespace Thor.Authorizer.Test.Tenancy;

public class TenantRoutingCacheTests
{
    private static readonly TenantRoutingCacheOptions Options = new() { Ttl = TimeSpan.FromMinutes(10) };

    [Fact]
    public async Task GetOrAddAsync_OnCacheHit_DoesNotCallRepositoryAgain()
    {
        var route = TenantRouteFixtures.Default();
        var repository = Substitute.For<ITenantRoutingRepository>();
        repository.GetBySubdomainAsync(route.Subdomain).Returns(route);
        var cache = new TenantRoutingCache(repository, Options, new FakeTimeProvider());

        await cache.GetOrAddAsync(route.Subdomain);
        await cache.GetOrAddAsync(route.Subdomain);

        await repository.Received(1).GetBySubdomainAsync(route.Subdomain);
    }

    [Fact]
    public async Task GetOrAddAsync_AfterTtlExpires_RefetchesFromRepository()
    {
        var route = TenantRouteFixtures.Default();
        var repository = Substitute.For<ITenantRoutingRepository>();
        repository.GetBySubdomainAsync(route.Subdomain).Returns(route);
        var timeProvider = new FakeTimeProvider();
        var cache = new TenantRoutingCache(repository, Options, timeProvider);

        await cache.GetOrAddAsync(route.Subdomain);
        timeProvider.Advance(TimeSpan.FromMinutes(11));
        await cache.GetOrAddAsync(route.Subdomain);

        await repository.Received(2).GetBySubdomainAsync(route.Subdomain);
    }

    [Fact]
    public async Task GetOrAddAsync_ForUnknownSubdomain_CachesNegativeResult()
    {
        var repository = Substitute.For<ITenantRoutingRepository>();
        repository.GetBySubdomainAsync("unknown").Returns((TenantRoute?)null);
        var cache = new TenantRoutingCache(repository, Options, new FakeTimeProvider());

        var first = await cache.GetOrAddAsync("unknown");
        var second = await cache.GetOrAddAsync("unknown");

        first.Should().BeNull();
        second.Should().BeNull();
        await repository.Received(1).GetBySubdomainAsync("unknown");
    }
}

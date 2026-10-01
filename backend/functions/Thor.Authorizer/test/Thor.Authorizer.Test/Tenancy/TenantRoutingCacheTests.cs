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
    public async Task GetByUserPoolIdAsync_OnCacheHit_DoesNotCallRepositoryAgain()
    {
        var route = TenantRouteFixtures.Default();
        var repository = Substitute.For<ITenantRoutingRepository>();
        repository.GetByUserPoolIdAsync(route.UserPoolId).Returns(route);
        var cache = new TenantRoutingCache(repository, Options, new FakeTimeProvider());

        await cache.GetByUserPoolIdAsync(route.UserPoolId);
        await cache.GetByUserPoolIdAsync(route.UserPoolId);

        await repository.Received(1).GetByUserPoolIdAsync(route.UserPoolId);
    }

    [Fact]
    public async Task GetByUserPoolIdAsync_AfterTtlExpires_RefetchesFromRepository()
    {
        var route = TenantRouteFixtures.Default();
        var repository = Substitute.For<ITenantRoutingRepository>();
        repository.GetByUserPoolIdAsync(route.UserPoolId).Returns(route);
        var timeProvider = new FakeTimeProvider();
        var cache = new TenantRoutingCache(repository, Options, timeProvider);

        await cache.GetByUserPoolIdAsync(route.UserPoolId);
        timeProvider.Advance(TimeSpan.FromMinutes(11));
        await cache.GetByUserPoolIdAsync(route.UserPoolId);

        await repository.Received(2).GetByUserPoolIdAsync(route.UserPoolId);
    }

    [Fact]
    public async Task GetByUserPoolIdAsync_ForUnknownPool_CachesNegativeResult()
    {
        var repository = Substitute.For<ITenantRoutingRepository>();
        repository.GetByUserPoolIdAsync("us-east-1_Unknown").Returns((TenantRoute?)null);
        var cache = new TenantRoutingCache(repository, Options, new FakeTimeProvider());

        var first = await cache.GetByUserPoolIdAsync("us-east-1_Unknown");
        var second = await cache.GetByUserPoolIdAsync("us-east-1_Unknown");

        first.Should().BeNull();
        second.Should().BeNull();
        await repository.Received(1).GetByUserPoolIdAsync("us-east-1_Unknown");
    }

    [Fact]
    public async Task GetByUserPoolIdAsync_PoolIdsDifferingOnlyByCase_AreDistinctEntries()
    {
        // Cognito pool ids are case-sensitive: a cached route for one must never answer the other.
        var route = TenantRouteFixtures.Default() with { UserPoolId = "us-east-1_AbC" };
        var repository = Substitute.For<ITenantRoutingRepository>();
        repository.GetByUserPoolIdAsync("us-east-1_AbC").Returns(route);
        repository.GetByUserPoolIdAsync("us-east-1_abc").Returns((TenantRoute?)null);
        var cache = new TenantRoutingCache(repository, Options, new FakeTimeProvider());

        (await cache.GetByUserPoolIdAsync("us-east-1_AbC")).Should().Be(route);
        (await cache.GetByUserPoolIdAsync("us-east-1_abc")).Should().BeNull();
    }

    [Fact]
    public async Task GetByTenantIdAsync_OnCacheHit_DoesNotCallRepositoryAgain()
    {
        var route = TenantRouteFixtures.Default();
        var repository = Substitute.For<ITenantRoutingRepository>();
        repository.GetByTenantIdAsync(route.TenantId).Returns(route);
        var cache = new TenantRoutingCache(repository, Options, new FakeTimeProvider());

        await cache.GetByTenantIdAsync(route.TenantId);
        await cache.GetByTenantIdAsync(route.TenantId);

        await repository.Received(1).GetByTenantIdAsync(route.TenantId);
    }

    [Fact]
    public async Task Lookups_ByPoolIdAndByTenantId_DoNotShareEntries()
    {
        // A value used as one kind of key must not hit an entry cached under the other kind.
        var route = TenantRouteFixtures.Default();
        var repository = Substitute.For<ITenantRoutingRepository>();
        repository.GetByUserPoolIdAsync("same-key").Returns(route);
        repository.GetByTenantIdAsync("same-key").Returns((TenantRoute?)null);
        var cache = new TenantRoutingCache(repository, Options, new FakeTimeProvider());

        await cache.GetByUserPoolIdAsync("same-key");
        var byTenant = await cache.GetByTenantIdAsync("same-key");

        byTenant.Should().BeNull();
        await repository.Received(1).GetByTenantIdAsync("same-key");
    }
}

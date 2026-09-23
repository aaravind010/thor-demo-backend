using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Thor.Api.Exceptions;
using Thor.Api.Models;
using Thor.Api.Services;
using Thor.Api.Test.TestFixtures;
using Thor.Auth;
using Thor.DataConnectionManager.Exceptions;
using Thor.DataLayer.Data;
using Thor.DataLayer.Models;

namespace Thor.Api.Test.Services;

/// <summary>
/// Exercises ConnectorApiKeyService's real issuance logic (hashing, scope lookup,
/// persistence) against an EF Core InMemory-backed MasterDbContext.
/// </summary>
public class ConnectorApiKeyServiceTests
{
    private const string Pepper = "unit-test-secret-pepper-0123456789abcdef";

    private static readonly MasterConnectionInfo ConnectionInfo = new("localhost", "unused", "unused", "unused");

    private static InMemoryMasterDbContextFactory NewFactory() => new(Guid.NewGuid().ToString());

    private static ConnectorApiKeyService CreateSut(InMemoryMasterDbContextFactory factory) =>
        new(factory, ConnectionInfo, new ConnectorSecurityOptions("unused", Pepper), NullLogger<ConnectorApiKeyService>.Instance);

    private static Guid SeedTenant(InMemoryMasterDbContextFactory factory)
    {
        var tenantId = Guid.NewGuid();
        using var db = factory.Create(ConnectionInfo);
        db.Tenants.Add(new Tenant
        {
            TenantId = tenantId,
            DisplayName = "tenant-1",
            Subdomain = $"tenant-{tenantId:N}",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        });
        db.SaveChanges();
        return tenantId;
    }

    private static void SeedScope(InMemoryMasterDbContextFactory factory, string scopeText)
    {
        using var db = factory.Create(ConnectionInfo);
        db.ApiScopes.Add(new ApiScope { ScopeText = scopeText });
        db.SaveChanges();
    }

    [Fact]
    public async Task CreateAsync_WithExistingScope_PersistsKeyWithGrantedScope()
    {
        var factory = NewFactory();
        var tenantId = SeedTenant(factory);
        SeedScope(factory, "scanner");
        var sut = CreateSut(factory);

        var response = await sut.CreateAsync(
            tenantId, new CreateConnectorApiKeyRequest("scanner", "My key", 0), CancellationToken.None);

        response.Scope.Should().Be("scanner");
        response.Label.Should().Be("My key");
        response.ExpiresAt.Should().BeNull();
        response.RawApiKey.Should().StartWith("thor_" + response.KeyId + "_");

        using var verifyDb = factory.Create(ConnectionInfo);
        var storedKey = await verifyDb.TenantApiKeys
            .Include(k => k.ScopeMaps).ThenInclude(m => m.Scope)
            .SingleAsync(k => k.KeyId == response.KeyId);
        storedKey.TenantId.Should().Be(tenantId);
        storedKey.StatusId.Should().Be(ApiKeyStatus.Active);
        storedKey.SecretHash.Should().NotBeNullOrEmpty();
        storedKey.ScopeMaps.Should().ContainSingle(m => m.Scope.ScopeText == "scanner");
        (await verifyDb.ApiScopes.CountAsync(s => s.ScopeText == "scanner")).Should().Be(1);
    }

    [Fact]
    public async Task CreateAsync_WithUnknownScope_ThrowsScopeNotFoundException()
    {
        var factory = NewFactory();
        var tenantId = SeedTenant(factory);
        var sut = CreateSut(factory);

        var act = () => sut.CreateAsync(
            tenantId, new CreateConnectorApiKeyRequest("scanner", null, 0), CancellationToken.None);

        await act.Should().ThrowAsync<ScopeNotFoundException>();

        using var verifyDb = factory.Create(ConnectionInfo);
        (await verifyDb.TenantApiKeys.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task CreateAsync_ReturnedRawKey_VerifiesAgainstStoredHash()
    {
        var factory = NewFactory();
        var tenantId = SeedTenant(factory);
        SeedScope(factory, "scanner");
        var sut = CreateSut(factory);

        var response = await sut.CreateAsync(
            tenantId, new CreateConnectorApiKeyRequest("scanner", null, 0), CancellationToken.None);

        PrefixedSecretToken.TryParse(response.RawApiKey, ApiKeyConstants.Prefix, out _, out var secret).Should().BeTrue();

        using var verifyDb = factory.Create(ConnectionInfo);
        var storedKey = await verifyDb.TenantApiKeys.SingleAsync(k => k.KeyId == response.KeyId);
        SecretHasher.Verify(Pepper, storedKey.Salt, secret, storedKey.SecretHash).Should().BeTrue();
    }

    [Fact]
    public async Task CreateAsync_WithPositiveExpiresInDays_SetsExpiresAt()
    {
        var factory = NewFactory();
        var tenantId = SeedTenant(factory);
        SeedScope(factory, "scanner");
        var sut = CreateSut(factory);

        var response = await sut.CreateAsync(
            tenantId, new CreateConnectorApiKeyRequest("scanner", null, 30), CancellationToken.None);

        response.ExpiresAt.Should().NotBeNull();
        response.ExpiresAt!.Value.Should().BeCloseTo(DateTimeOffset.UtcNow.AddDays(30), TimeSpan.FromMinutes(1));
    }

    [Fact]
    public async Task CreateAsync_WithUnknownTenant_ThrowsTenantNotFoundException()
    {
        var sut = CreateSut(NewFactory());

        var act = () => sut.CreateAsync(
            Guid.NewGuid(), new CreateConnectorApiKeyRequest("scanner", null, 0), CancellationToken.None);

        await act.Should().ThrowAsync<TenantNotFoundException>();
    }
}

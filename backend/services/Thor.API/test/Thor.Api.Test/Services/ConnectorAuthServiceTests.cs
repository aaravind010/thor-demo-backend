using System.IdentityModel.Tokens.Jwt;
using System.Security.Cryptography;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Thor.Api.Constants;
using Thor.Api.Exceptions;
using Thor.Api.Services;
using Thor.Api.Test.TestFixtures;
using Thor.Auth;
using Thor.DataLayer.Data;
using Thor.DataLayer.Models;

namespace Thor.Api.Test.Services;

/// <summary>
/// Exercises ConnectorAuthService's real register/refresh logic (hash verification, scope checks,
/// token issuance, refresh-token rotation) against an EF Core InMemory-backed MasterDbContext —
/// no Postgres required, but every code path runs for real instead of being mocked away.
/// </summary>
public class ConnectorAuthServiceTests
{
    private const string Pepper = "unit-test-secret-pepper-0123456789abcdef";
    private const string RoleType = "scanner";

    // RSA is a per-call credential (ADR §5.2), not a shared secret, so a fresh key pair is
    // generated for the test run rather than checked in.
    private static readonly string PrivateKeyPem = RSA.Create(2048).ExportPkcs8PrivateKeyPem();

    private static readonly MasterConnectionInfo ConnectionInfo = new("localhost", "unused", "unused", "unused");

    private static InMemoryMasterDbContextFactory NewFactory() => new(Guid.NewGuid().ToString());

    private static ConnectorAuthService CreateSut(InMemoryMasterDbContextFactory factory) =>
        new(factory, ConnectionInfo, new ConnectorSecurityOptions(PrivateKeyPem, Pepper), new ThorTokenIssuer());

    private static (string RawKey, TenantApiKey Entity) IssueApiKey(
        short statusId = ApiKeyStatus.Active, DateTimeOffset? expiresAt = null)
    {
        var keyId = Guid.NewGuid();
        var secret = SecretHasher.GenerateSecret();
        var salt = SecretHasher.GenerateSalt();

        var entity = new TenantApiKey
        {
            KeyId = keyId,
            TenantId = Guid.NewGuid(),
            SecretHash = SecretHasher.ComputeHash(Pepper, salt, secret),
            Salt = salt,
            StatusId = statusId,
            CreatedAt = DateTimeOffset.UtcNow,
            ExpiresAt = expiresAt,
        };

        return (PrefixedSecretToken.Format(ApiKeyConstants.Prefix, keyId, secret), entity);
    }

    private static void Seed(InMemoryMasterDbContextFactory factory, TenantApiKey apiKey, params string[] grantedScopes)
    {
        using var db = factory.Create(ConnectionInfo);
        db.TenantApiKeys.Add(apiKey);

        short scopeId = 1;
        foreach (var scopeText in grantedScopes)
        {
            db.ApiScopes.Add(new ApiScope { Id = scopeId, ScopeText = scopeText });
            db.KeyScopeMaps.Add(new KeyScopeMap { Id = scopeId, ApiKeyUuid = apiKey.KeyId, ScopeId = scopeId });
            scopeId++;
        }

        db.SaveChanges();
    }

    [Fact]
    public async Task RegisterAsync_WithValidKeyAndGrantedScope_IssuesTokensAndUpdatesLastUsed()
    {
        var factory = NewFactory();
        var (rawKey, apiKey) = IssueApiKey();
        Seed(factory, apiKey, RoleType);
        var sut = CreateSut(factory);

        var response = await sut.RegisterAsync(rawKey, RoleType, CancellationToken.None);

        response.RoleType.Should().Be(RoleType);
        response.Token.Should().NotBeNullOrWhiteSpace();
        response.RefreshToken.Should().StartWith(ThorTokenIssuer.RefreshTokenPrefix + "_");

        using var verifyDb = factory.Create(ConnectionInfo);
        var storedKey = await verifyDb.TenantApiKeys.SingleAsync(k => k.KeyId == apiKey.KeyId);
        storedKey.LastUsedAt.Should().NotBeNull();
        (await verifyDb.TaskApiRefreshTokens.CountAsync(t => t.KeyId == apiKey.KeyId)).Should().Be(1);
    }

    [Fact]
    public async Task RegisterAsync_WithValidKeyAndGrantedScope_IssuesTokenWithExpectedClaims()
    {
        var factory = NewFactory();
        var (rawKey, apiKey) = IssueApiKey();
        Seed(factory, apiKey, RoleType);
        var sut = CreateSut(factory);

        var response = await sut.RegisterAsync(rawKey, RoleType, CancellationToken.None);

        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(response.Token);
        jwt.Issuer.Should().Be(AuthConstants.JwtIssuer);
        jwt.Audiences.Should().ContainSingle().Which.Should().Be(AuthConstants.TaskApiAudience);
        jwt.Claims.Should().ContainSingle(c => c.Type == ThorTokenIssuer.SubjectClaimType && c.Value == apiKey.KeyId.ToString());
        jwt.Claims.Should().ContainSingle(c => c.Type == ThorTokenIssuer.TenantIdClaimType && c.Value == apiKey.TenantId.ToString());
        jwt.Claims.Should().ContainSingle(c => c.Type == ThorTokenIssuer.ScopeClaimType && c.Value == RoleType);
    }

    [Fact]
    public async Task RegisterAsync_WithMalformedApiKey_ThrowsInvalidApiKeyException()
    {
        var sut = CreateSut(NewFactory());

        var act = () => sut.RegisterAsync("not-a-valid-key", RoleType, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidApiKeyException>();
    }

    [Fact]
    public async Task RegisterAsync_WithUnknownKeyId_ThrowsInvalidApiKeyException()
    {
        var factory = NewFactory();
        var (rawKey, _) = IssueApiKey(); // deliberately never seeded
        var sut = CreateSut(factory);

        var act = () => sut.RegisterAsync(rawKey, RoleType, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidApiKeyException>();
    }

    [Fact]
    public async Task RegisterAsync_WithWrongSecret_ThrowsInvalidApiKeyException()
    {
        var factory = NewFactory();
        var (_, apiKey) = IssueApiKey();
        Seed(factory, apiKey, RoleType);
        var sut = CreateSut(factory);
        var tamperedKey = PrefixedSecretToken.Format(ApiKeyConstants.Prefix, apiKey.KeyId, SecretHasher.GenerateSecret());

        var act = () => sut.RegisterAsync(tamperedKey, RoleType, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidApiKeyException>();
    }

    [Theory]
    [InlineData(ApiKeyStatus.Revoked)]
    [InlineData(ApiKeyStatus.Expired)]
    public async Task RegisterAsync_WithNonActiveKeyStatus_ThrowsInvalidApiKeyException(short statusId)
    {
        var factory = NewFactory();
        var (rawKey, apiKey) = IssueApiKey(statusId);
        Seed(factory, apiKey, RoleType);
        var sut = CreateSut(factory);

        var act = () => sut.RegisterAsync(rawKey, RoleType, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidApiKeyException>();
    }

    [Fact]
    public async Task RegisterAsync_WithExpiredKey_ThrowsInvalidApiKeyException()
    {
        var factory = NewFactory();
        var (rawKey, apiKey) = IssueApiKey(expiresAt: DateTimeOffset.UtcNow.AddMinutes(-1));
        Seed(factory, apiKey, RoleType);
        var sut = CreateSut(factory);

        var act = () => sut.RegisterAsync(rawKey, RoleType, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidApiKeyException>();
    }

    [Fact]
    public async Task RegisterAsync_WithUngrantedRoleType_ThrowsScopeNotGrantedExceptionForThatRole()
    {
        var factory = NewFactory();
        var (rawKey, apiKey) = IssueApiKey();
        Seed(factory, apiKey, RoleType);
        var sut = CreateSut(factory);

        var act = () => sut.RegisterAsync(rawKey, "uploader", CancellationToken.None);

        var thrown = await act.Should().ThrowAsync<ScopeNotGrantedException>();
        thrown.Which.RoleType.Should().Be("uploader");
    }

    [Fact]
    public async Task RefreshAsync_WithValidToken_RotatesRefreshTokenAndRevokesOld()
    {
        var factory = NewFactory();
        var (rawKey, apiKey) = IssueApiKey();
        Seed(factory, apiKey, RoleType);
        var sut = CreateSut(factory);
        var initial = await sut.RegisterAsync(rawKey, RoleType, CancellationToken.None);

        var refreshed = await sut.RefreshAsync(initial.RefreshToken, CancellationToken.None);

        refreshed.RoleType.Should().Be(RoleType);
        refreshed.RefreshToken.Should().NotBe(initial.RefreshToken);

        PrefixedSecretToken.TryParse(initial.RefreshToken, ThorTokenIssuer.RefreshTokenPrefix, out var oldTokenId, out _);
        using var verifyDb = factory.Create(ConnectionInfo);
        var oldToken = await verifyDb.TaskApiRefreshTokens.SingleAsync(t => t.TokenId == oldTokenId);
        oldToken.RevokedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task RefreshAsync_WithMalformedToken_ThrowsInvalidRefreshTokenException()
    {
        var sut = CreateSut(NewFactory());

        var act = () => sut.RefreshAsync("not-a-refresh-token", CancellationToken.None);

        await act.Should().ThrowAsync<InvalidRefreshTokenException>();
    }

    [Fact]
    public async Task RefreshAsync_WhenTokenAlreadySpent_ThrowsInvalidRefreshTokenException()
    {
        var factory = NewFactory();
        var (rawKey, apiKey) = IssueApiKey();
        Seed(factory, apiKey, RoleType);
        var sut = CreateSut(factory);
        var initial = await sut.RegisterAsync(rawKey, RoleType, CancellationToken.None);
        await sut.RefreshAsync(initial.RefreshToken, CancellationToken.None); // spends the initial token

        var act = () => sut.RefreshAsync(initial.RefreshToken, CancellationToken.None); // reuse

        await act.Should().ThrowAsync<InvalidRefreshTokenException>();
    }

    [Fact]
    public async Task RefreshAsync_WhenUnderlyingKeyNoLongerUsable_ThrowsInvalidApiKeyException()
    {
        var factory = NewFactory();
        var (rawKey, apiKey) = IssueApiKey();
        Seed(factory, apiKey, RoleType);
        var sut = CreateSut(factory);
        var initial = await sut.RegisterAsync(rawKey, RoleType, CancellationToken.None);

        using (var db = factory.Create(ConnectionInfo))
        {
            var stored = await db.TenantApiKeys.SingleAsync(k => k.KeyId == apiKey.KeyId);
            stored.StatusId = ApiKeyStatus.Revoked;
            await db.SaveChangesAsync();
        }

        var act = () => sut.RefreshAsync(initial.RefreshToken, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidApiKeyException>();
    }
}

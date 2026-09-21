using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using FluentAssertions;
using Microsoft.IdentityModel.Tokens;

namespace Thor.Auth.Test;

/// <summary>
/// Exercises ThorTokenValidator's checks (ADR §5.2): signature, expiry, audience, algorithm,
/// and scope presence — against tokens shaped exactly like the ones ThorTokenIssuer mints.
/// Uses a fresh RSA key pair generated per test run rather than a checked-in key, since these
/// are asymmetric credentials, not shared secrets.
/// </summary>
public class ThorTokenValidatorTests
{
    private const string Issuer = "thor-task-api";
    private const string Scope = "scanner";

    private static readonly Guid KeyId = Guid.NewGuid();
    private static readonly Guid TenantId = Guid.NewGuid();

    private static readonly RSA KeyPair = RSA.Create(2048);
    private static readonly string PrivateKeyPem = KeyPair.ExportPkcs8PrivateKeyPem();
    private static readonly string PublicKeyPem = KeyPair.ExportSubjectPublicKeyInfoPem();

    private static ThorTokenValidator CreateSut() => new(PublicKeyPem);

    private static string IssueValidToken() =>
        new ThorTokenIssuer().IssueAccessToken(
            PrivateKeyPem, Issuer, TokenAudiences.TaskApi, TimeSpan.FromMinutes(15), KeyId, TenantId, Scope);

    [Fact]
    public void Validate_WithWellFormedToken_ReturnsSuccessWithExpectedClaims()
    {
        var token = IssueValidToken();
        var sut = CreateSut();

        var result = sut.Validate(token);

        result.IsValid.Should().BeTrue();
        result.KeyId.Should().Be(KeyId);
        result.TenantId.Should().Be(TenantId);
        result.Scope.Should().Be(Scope);
    }

    [Fact]
    public void Validate_WithWrongSigningKey_ReturnsInvalid()
    {
        var token = IssueValidToken();
        using var otherKeyPair = RSA.Create(2048);
        var sut = new ThorTokenValidator(otherKeyPair.ExportSubjectPublicKeyInfoPem());

        var result = sut.Validate(token);

        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public void Validate_WithExpiredToken_ReturnsInvalid()
    {
        var claims = new[]
        {
            new Claim(ThorTokenIssuer.SubjectClaimType, KeyId.ToString()),
            new Claim(ThorTokenIssuer.TenantIdClaimType, TenantId.ToString()),
            new Claim(ThorTokenIssuer.ScopeClaimType, Scope),
        };
        var signingCredentials = new SigningCredentials(new RsaSecurityKey(KeyPair), SecurityAlgorithms.RsaSha256);
        var expiredToken = new JwtSecurityToken(
            issuer: Issuer,
            audience: TokenAudiences.TaskApi,
            claims: claims,
            notBefore: DateTime.UtcNow.AddMinutes(-30),
            expires: DateTime.UtcNow.AddMinutes(-15),
            signingCredentials: signingCredentials);
        var token = new JwtSecurityTokenHandler().WriteToken(expiredToken);
        var sut = CreateSut();

        var result = sut.Validate(token);

        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public void Validate_WithWrongAudience_ReturnsInvalid()
    {
        var token = new ThorTokenIssuer().IssueAccessToken(
            PrivateKeyPem, Issuer, "some-other-service", TimeSpan.FromMinutes(15), KeyId, TenantId, Scope);
        var sut = CreateSut();

        var result = sut.Validate(token);

        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public void Validate_WithoutScopeClaim_ReturnsInvalid()
    {
        var claims = new[]
        {
            new Claim(ThorTokenIssuer.SubjectClaimType, KeyId.ToString()),
            new Claim(ThorTokenIssuer.TenantIdClaimType, TenantId.ToString()),
        };
        var token = JwtTokenIssuer.Issue(PrivateKeyPem, Issuer, TokenAudiences.TaskApi, TimeSpan.FromMinutes(15), claims);
        var sut = CreateSut();

        var result = sut.Validate(token);

        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public void Validate_WithHmacSignedToken_ReturnsInvalid()
    {
        // Algorithm-confusion attack: sign with HS256 using the (public, non-secret) key's
        // exported bytes as the HMAC secret. Must be rejected — ValidAlgorithms pins RS256.
        var claims = new[]
        {
            new Claim(ThorTokenIssuer.SubjectClaimType, KeyId.ToString()),
            new Claim(ThorTokenIssuer.TenantIdClaimType, TenantId.ToString()),
            new Claim(ThorTokenIssuer.ScopeClaimType, Scope),
        };
        var hmacKeyBytes = System.Text.Encoding.UTF8.GetBytes(PublicKeyPem)[..32];
        var signingCredentials = new SigningCredentials(new SymmetricSecurityKey(hmacKeyBytes), SecurityAlgorithms.HmacSha256);
        var forgedToken = new JwtSecurityToken(
            issuer: Issuer,
            audience: TokenAudiences.TaskApi,
            claims: claims,
            notBefore: DateTime.UtcNow,
            expires: DateTime.UtcNow.AddMinutes(15),
            signingCredentials: signingCredentials);
        var token = new JwtSecurityTokenHandler().WriteToken(forgedToken);
        var sut = CreateSut();

        var result = sut.Validate(token);

        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public void Validate_WithWrongIssuer_ReturnsInvalid()
    {
        var token = new ThorTokenIssuer().IssueAccessToken(
            PrivateKeyPem, "some-other-issuer", TokenAudiences.TaskApi, TimeSpan.FromMinutes(15), KeyId, TenantId, Scope);
        var sut = CreateSut();

        var result = sut.Validate(token);

        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public void Validate_WithMalformedToken_ReturnsInvalid()
    {
        var sut = CreateSut();

        var result = sut.Validate("not-a-jwt");

        result.IsValid.Should().BeFalse();
    }
}

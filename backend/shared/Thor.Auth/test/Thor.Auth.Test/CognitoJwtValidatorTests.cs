using System.Security.Cryptography;
using FluentAssertions;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using NSubstitute;

namespace Thor.Auth.Test;

public class CognitoJwtValidatorTests : IDisposable
{
    private const string UserPoolId = "us-east-1_ExamplePool";
    private const string AppClientId = "client-abc123";
    private const string Region = "us-east-1";

    private readonly RSA _rsa = RSA.Create(2048);
    private readonly IJwksProvider _jwksProvider;
    private readonly CognitoJwtValidator _validator;

    public CognitoJwtValidatorTests()
    {
        var rsaKey = new RsaSecurityKey(_rsa) { KeyId = "test-key-1" };
        var jwk = JsonWebKeyConverter.ConvertFromRSASecurityKey(rsaKey);
        jwk.Alg = "RS256";

        _jwksProvider = Substitute.For<IJwksProvider>();
        _jwksProvider.GetJwksAsync(UserPoolId, Region)
            .Returns(new JsonWebKeySet { Keys = { jwk } });

        _validator = new CognitoJwtValidator(_jwksProvider);
    }

    public void Dispose() => _rsa.Dispose();

    private string CreateToken(Dictionary<string, object> claims)
    {
        var rsaKey = new RsaSecurityKey(_rsa) { KeyId = "test-key-1" };
        var signingCredentials = new SigningCredentials(rsaKey, SecurityAlgorithms.RsaSha256);

        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = $"https://cognito-idp.{Region}.amazonaws.com/{UserPoolId}",
            SigningCredentials = signingCredentials,
            Claims = claims,
            NotBefore = DateTime.UtcNow.AddMinutes(-1),
            Expires = DateTime.UtcNow.AddHours(1),
        };

        return new JsonWebTokenHandler().CreateToken(descriptor);
    }

    private Dictionary<string, object> BaseClaims(string scope = "") => new()
    {
        ["token_use"] = "access",
        ["client_id"] = AppClientId,
        ["sub"] = "user-123",
        ["scope"] = scope,
    };

    [Fact]
    public async Task ValidateAsync_ValidAccessToken_Succeeds()
    {
        var token = CreateToken(BaseClaims(scope: "read:orders"));

        var result = await _validator.ValidateAsync(token, UserPoolId, AppClientId, Region);

        result.IsValid.Should().BeTrue();
        result.PrincipalId.Should().Be("user-123");
        result.Scopes.Should().Contain("read:orders");
    }

    [Fact]
    public async Task ValidateAsync_IdToken_IsRejected()
    {
        var claims = BaseClaims();
        claims["token_use"] = "id";
        var token = CreateToken(claims);

        var result = await _validator.ValidateAsync(token, UserPoolId, AppClientId, Region);

        result.IsValid.Should().BeFalse();
        result.FailureReason.Should().Be("not an access token");
    }

    [Fact]
    public async Task ValidateAsync_ClientIdMismatch_Fails()
    {
        var claims = BaseClaims();
        claims["client_id"] = "some-other-client";
        var token = CreateToken(claims);

        var result = await _validator.ValidateAsync(token, UserPoolId, AppClientId, Region);

        result.IsValid.Should().BeFalse();
        result.FailureReason.Should().Be("client_id mismatch");
    }

    [Fact]
    public async Task ValidateAsync_ExpiredBeyondClockSkew_Fails()
    {
        var rsaKey = new RsaSecurityKey(_rsa) { KeyId = "test-key-1" };
        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = $"https://cognito-idp.{Region}.amazonaws.com/{UserPoolId}",
            SigningCredentials = new SigningCredentials(rsaKey, SecurityAlgorithms.RsaSha256),
            Claims = BaseClaims(),
            NotBefore = DateTime.UtcNow.AddHours(-2),
            Expires = DateTime.UtcNow.AddMinutes(-5),
        };
        var token = new JsonWebTokenHandler().CreateToken(descriptor);

        var result = await _validator.ValidateAsync(token, UserPoolId, AppClientId, Region);

        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public async Task ValidateAsync_SignedByWrongKey_Fails()
    {
        using var otherRsa = RSA.Create(2048);
        var otherKey = new RsaSecurityKey(otherRsa) { KeyId = "test-key-1" };
        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = $"https://cognito-idp.{Region}.amazonaws.com/{UserPoolId}",
            SigningCredentials = new SigningCredentials(otherKey, SecurityAlgorithms.RsaSha256),
            Claims = BaseClaims(),
            NotBefore = DateTime.UtcNow.AddMinutes(-1),
            Expires = DateTime.UtcNow.AddHours(1),
        };
        var token = new JsonWebTokenHandler().CreateToken(descriptor);

        var result = await _validator.ValidateAsync(token, UserPoolId, AppClientId, Region);

        result.IsValid.Should().BeFalse();
    }
}

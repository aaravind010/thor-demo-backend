using System.Security.Cryptography;
using FluentAssertions;

namespace Thor.Auth.Test;

/// <summary>
/// JwtIssuerReader is a routing hint only — it must decode a well-formed token's issuer without
/// verifying anything, and never throw on malformed input (the Lambda authorizer falls back to
/// the Cognito path whenever this returns null).
/// </summary>
public class JwtIssuerReaderTests
{
    [Fact]
    public void TryReadIssuer_WithWellFormedToken_ReturnsIssuer()
    {
        var token = new ThorTokenIssuer().IssueAccessToken(
            RSA.Create(2048).ExportPkcs8PrivateKeyPem(), TokenIssuers.ThorTaskApi, TokenAudiences.TaskApi,
            TimeSpan.FromMinutes(15), Guid.NewGuid(), Guid.NewGuid(), "scanner");

        JwtIssuerReader.TryReadIssuer(token).Should().Be(TokenIssuers.ThorTaskApi);
    }

    [Theory]
    [InlineData("not-a-jwt")]
    [InlineData("")]
    [InlineData("a.b")]
    [InlineData("a.b.c")]
    public void TryReadIssuer_WithMalformedToken_ReturnsNull(string token)
    {
        JwtIssuerReader.TryReadIssuer(token).Should().BeNull();
    }
}

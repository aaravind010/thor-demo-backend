using FluentAssertions;
using Thor.Authorizer.Core;

namespace Thor.Authorizer.Test;

public class CredentialClassifierTests
{
    [Fact]
    public void Classify_ApiKeyHeader_ReturnsApiKeyWithMaterial()
    {
        var result = CredentialClassifier.Classify("ApiKey abc123.secretvalue");

        result.Type.Should().Be(CredentialType.ApiKey);
        result.Material.Should().Be("abc123.secretvalue");
    }

    [Fact]
    public void Classify_BearerJwt_ReturnsJwtWithMaterial()
    {
        var jwt = "eyJhbGciOiJSUzI1NiJ9.eyJzdWIiOiIxMjMifQ.c2ln";

        var result = CredentialClassifier.Classify($"Bearer {jwt}");

        result.Type.Should().Be(CredentialType.Jwt);
        result.Material.Should().Be(jwt);
    }

    [Fact]
    public void Classify_BareJwtWithoutScheme_ReturnsJwt()
    {
        var jwt = "eyJhbGciOiJSUzI1NiJ9.eyJzdWIiOiIxMjMifQ.c2ln";

        var result = CredentialClassifier.Classify(jwt);

        result.Type.Should().Be(CredentialType.Jwt);
    }

    [Fact]
    public void Classify_ApiKeySchemeWithEmptyMaterial_ReturnsUnknown()
    {
        CredentialClassifier.Classify("ApiKey ").Type.Should().Be(CredentialType.Unknown);
    }

    [Fact]
    public void Classify_TwoDotGarbage_ReturnsUnknown()
    {
        CredentialClassifier.Classify("not.a.jwt!!!").Type.Should().Be(CredentialType.Unknown);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Classify_NullOrEmptyHeader_ReturnsUnknown(string? header)
    {
        CredentialClassifier.Classify(header).Type.Should().Be(CredentialType.Unknown);
    }
}

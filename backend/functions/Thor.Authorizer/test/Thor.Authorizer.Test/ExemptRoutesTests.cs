using FluentAssertions;
using Thor.Authorizer.Core;

namespace Thor.Authorizer.Test;

public class ExemptRoutesTests
{
    [Theory]
    [InlineData("GET", "/health")]
    [InlineData("GET", "/health/")]
    [InlineData("POST", "/health")] // method doesn't matter for health
    [InlineData("GET", "/scalar")]
    [InlineData("GET", "/scalar/")]
    [InlineData("GET", "/scalar/v1")]
    [InlineData("GET", "/scalar/anything/nested")]
    [InlineData("POST", "/v1/connector-api-keys")]
    [InlineData("POST", "/v2/connector-api-keys")]
    [InlineData("post", "/v1/connector-api-keys")] // method match is case-insensitive
    [InlineData(null, "/health")] // method doesn't matter for health
    public void SkipsCredentialValidation_ForExemptRoute_ReturnsTrue(string? method, string path)
    {
        ExemptRoutes.SkipsCredentialValidation(method, path).Should().BeTrue();
    }

    [Theory]
    [InlineData("GET", "/v1/connector-api-keys")] // connector-api-keys is POST-only
    [InlineData("DELETE", "/v1/connector-api-keys")]
    [InlineData("POST", "/v1/connector-api-keys/extra")]
    [InlineData("POST", "/connector-api-keys")] // missing version segment
    [InlineData("GET", "/v1/orders")]
    [InlineData("GET", "/healthy")] // not an exact "/health" match
    [InlineData("GET", "/scalars")] // not an exact "/scalar" prefix match
    [InlineData("GET", null)]
    [InlineData("GET", "")]
    public void SkipsCredentialValidation_ForNonExemptRoute_ReturnsFalse(string? method, string? path)
    {
        ExemptRoutes.SkipsCredentialValidation(method, path).Should().BeFalse();
    }
}

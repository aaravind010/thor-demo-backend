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
    [InlineData("post", "/v1/register")] // method match is case-insensitive
    [InlineData("POST", "/v1/register")]
    [InlineData("POST", "/v2/register")]
    [InlineData(null, "/health")] // method doesn't matter for health
    [InlineData("GET", "/dev/health")]
    [InlineData("GET", "/qa/health")]
    [InlineData("GET", "/prod/health")]
    [InlineData("GET", "/dev/scalar")]
    [InlineData("GET", "/qa/scalar/")]
    [InlineData("GET", "/PROD/scalar/anything/nested")] // stage prefix match is case-insensitive
    [InlineData("POST", "/dev/v1/register")]
    [InlineData("POST", "/qa/v1/register")]
    [InlineData("POST", "/prod/v1/register")]
    [InlineData("POST", "/v1/connector-api-keys")] // TEMPORARY until Cognito is live
    [InlineData("POST", "/dev/v1/connector-api-keys")]
    public void SkipsCredentialValidation_ForExemptRoute_ReturnsTrue(string? method, string path)
    {
        ExemptRoutes.SkipsCredentialValidation(method, path).Should().BeTrue();
    }

    [Theory]
    [InlineData("GET", "/v1/connector-api-keys")] // connector-api-keys is POST-only
    [InlineData("POST", "/v1/connector-api-keys/extra")]
    [InlineData("POST", "/connector-api-keys")] // missing version segment
    [InlineData("GET", "/v1/register")] // register is POST-only
    [InlineData("POST", "/v1/register/extra")]
    [InlineData("POST", "/register")] // missing version segment
    [InlineData("GET", "/v1/orders")]
    [InlineData("GET", "/healthy")] // not an exact "/health" match
    [InlineData("GET", "/scalars")] // not an exact "/scalar" prefix match
    [InlineData("GET", "/staging/health")] // "staging" is not a recognized stage prefix
    [InlineData("GET", null)]
    [InlineData("GET", "")]
    public void SkipsCredentialValidation_ForNonExemptRoute_ReturnsFalse(string? method, string? path)
    {
        ExemptRoutes.SkipsCredentialValidation(method, path).Should().BeFalse();
    }
}

using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Thor.Api.Constants;
using Thor.Api.Services;
using Thor.Api.Test.TestFixtures;
using Thor.Auth;
using Thor.DataConnectionManager.Routing;
using Thor.DataLayer.Models;

namespace Thor.Api.Test;

[Collection(ThorApiHostCollection.Name)]
public class ApiVersioningEndpointTests : IClassFixture<ThorApiWebApplicationFactory>
{
    private const string CognitoToken = "cognito-access-token";

    private static readonly Guid TenantId = Guid.NewGuid();

    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _client;

    public ApiVersioningEndpointTests(ThorApiWebApplicationFactory factory)
    {
        // Gated routes need a credential for CognitoAuthMiddleware; these tests are about routing,
        // so Cognito validation is stubbed to accept CognitoToken for TenantId. The Thor-issued
        // token path keeps the real ThorTokenValidator.
        var cognitoValidator = Substitute.For<ICognitoValidator>();
        cognitoValidator.ValidateAsync(CognitoToken, "us-east-1_pool", "client-1", "us-east-1")
            .Returns(JwtValidationResult.Success("user-1", []));

        var routingResolver = Substitute.For<ITenantRoutingResolver>();
        routingResolver.ResolveAsync(TenantId, Arg.Any<CancellationToken>()).Returns(new TenantRouting
        {
            TenantId = TenantId, UserPoolId = "us-east-1_pool", AppClientId = "client-1", Region = "us-east-1",
        });

        _factory = factory.WithWebHostBuilder(builder => builder
            .UseEnvironment("Development")
            .ConfigureTestServices(services =>
            {
                services.AddSingleton(cognitoValidator);
                services.AddSingleton(routingResolver);
            }));

        _client = _factory.CreateClient();
    }

    private static HttpRequestMessage Authenticated(HttpMethod method, string path, object body, string token = CognitoToken)
    {
        var request = new HttpRequestMessage(method, path) { Content = JsonContent.Create(body) };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Add(TenantConstants.TenantHeaderName, TenantId.ToString());
        return request;
    }

    [Theory]
    [InlineData("/v1/scan")]
    [InlineData("/v1/identities")]
    public async Task GatedRoute_WithoutBearerToken_Returns401(string path)
    {
        var response = await _client.PostAsJsonAsync(path, new { });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Scan_WithThorIssuedToken_PassesAuthGate()
    {
        // The scan scheduler authenticates with a connector JWT minted by POST /v1/register;
        // proves Program.cs derives the validator's public key from the signing key.
        var securityOptions = _factory.Services.GetRequiredService<ConnectorSecurityOptions>();
        var token = _factory.Services.GetRequiredService<ITokenIssuer>().IssueAccessToken(
            securityOptions.JwtPrivateKeyPem, TokenIssuers.ThorTaskApi, TokenAudiences.TaskApi,
            TimeSpan.FromMinutes(5), Guid.NewGuid(), TenantId, "scanner");

        var response = await _client.SendAsync(
            Authenticated(HttpMethod.Post, "/v1/scan", new { scanConfigId = Guid.Empty }, token));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest); // reached the controller (missing ScanType)
    }

    [Fact]
    public async Task Register_OnUnversionedRoute_ReturnsNotFound()
    {
        var response = await _client.SendAsync(Authenticated(HttpMethod.Post, "/register", new { roleType = "scanner" }));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task RefreshRegister_OnUnversionedRoute_ReturnsNotFound()
    {
        var response = await _client.SendAsync(Authenticated(HttpMethod.Post, "/register/refresh", new { }));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Register_OnV1Route_IsReachedAndReportsVersion()
    {
        var response = await _client.PostAsJsonAsync("/v1/register", new { roleType = "scanner" });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        response.Headers.GetValues("api-version").Should().ContainSingle().Which.Should().Be("1");
        response.Headers.GetValues("api-supported-versions").Should().ContainSingle().Which.Should().Be("1.0");
    }

    [Fact]
    public async Task ConnectorApiKeys_OnV1RouteWithoutBearerToken_PassesAuthGate()
    {
        // TEMPORARY exemption until Cognito is live — remove this test with it.
        var response = await _client.PostAsJsonAsync("/v1/connector-api-keys", new { scope = "scanner" });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest); // reached the controller (missing tenant header)
    }

    [Theory]
    [InlineData("/scan-config")]
    [InlineData("/scan")]
    public async Task Scan_OnUnversionedRoute_ReturnsNotFound(string path)
    {
        var response = await _client.SendAsync(Authenticated(HttpMethod.Post, path, new { }));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task ScanConfig_OnV1Route_IsReachedAndReportsVersion()
    {
        var response = await _client.SendAsync(Authenticated(
            HttpMethod.Post, "/v1/scan-config", new { name = "scan-config-1", sourceIds = new[] { Guid.NewGuid() }, configValues = Array.Empty<object>() }));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        response.Headers.GetValues("api-version").Should().ContainSingle().Which.Should().Be("1");
    }

    [Fact]
    public async Task Scan_OnV1Route_IsReachedAndReportsVersion()
    {
        var response = await _client.SendAsync(Authenticated(HttpMethod.Post, "/v1/scan", new { scanConfigId = Guid.Empty }));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        response.Headers.GetValues("api-version").Should().ContainSingle().Which.Should().Be("1");
    }

    [Fact]
    public async Task OpenApiDocument_IsGeneratedPerVersion()
    {
        var response = await _client.GetAsync("/openapi/v1.json");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var document = await response.Content.ReadAsStringAsync();
        document.Should().Contain("/v1/register");
    }
}

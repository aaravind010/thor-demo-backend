using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Thor.Api.Test;

public class ApiVersioningEndpointTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client;

    public ApiVersioningEndpointTests(WebApplicationFactory<Program> factory)
    {
        _client = factory
            .WithWebHostBuilder(builder => builder.UseEnvironment("Development"))
            .CreateClient();
    }

    [Fact]
    public async Task Register_OnUnversionedRoute_ReturnsNotFound()
    {
        var response = await _client.PostAsJsonAsync("/register", new { roleType = "scanner" });

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task RefreshRegister_OnUnversionedRoute_ReturnsNotFound()
    {
        var response = await _client.PostAsJsonAsync("/register/refresh", new { });

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
    public async Task OpenApiDocument_IsGeneratedPerVersion()
    {
        var response = await _client.GetAsync("/openapi/v1.json");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var document = await response.Content.ReadAsStringAsync();
        document.Should().Contain("/v1/register");
    }
}

using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using System.Security.Cryptography;
using Thor.Api.Constants;
using Thor.Api.Controllers.V1;
using Thor.Api.Models;
using Thor.Api.Services;
using Thor.Api.Test.TestFixtures;
using Thor.Auth;
using Thor.DataLayer.Data;
using Thor.DataLayer.Models;

namespace Thor.Api.Test.Controllers.V1;

/// <summary>
/// Exercises RegisterController's own logic — header/body validation and the
/// exception-to-HTTP-status mapping — wired to a real ConnectorAuthService backed by an EF Core
/// InMemory database, rather than a mock, so every code path runs for real.
/// </summary>
public class RegisterControllerTests
{
    private const string Pepper = "unit-test-secret-pepper-0123456789abcdef";
    private const string RoleType = "scanner";

    // RSA is a per-call credential (ADR §5.2), not a shared secret, so a fresh key pair is
    // generated for the test run rather than checked in.
    private static readonly string PrivateKeyPem = RSA.Create(2048).ExportPkcs8PrivateKeyPem();

    private static readonly MasterConnectionInfo ConnectionInfo = new("localhost", "unused", "unused", "unused");

    private static (RegisterController Controller, InMemoryMasterDbContextFactory Factory) CreateController()
    {
        var factory = new InMemoryMasterDbContextFactory(Guid.NewGuid().ToString());
        var service = new ConnectorAuthService(factory, ConnectionInfo, new ConnectorSecurityOptions(PrivateKeyPem, Pepper), new ThorTokenIssuer());
        return (new RegisterController(service), factory);
    }

    private static (string RawKey, TenantApiKey Entity) IssueApiKey()
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
            StatusId = ApiKeyStatus.Active,
            CreatedAt = DateTimeOffset.UtcNow,
        };

        return (PrefixedSecretToken.Format(ApiKeyConstants.Prefix, keyId, secret), entity);
    }

    private static void SeedGrantedScope(InMemoryMasterDbContextFactory factory, TenantApiKey apiKey, string scopeText)
    {
        using var db = factory.Create(ConnectionInfo);
        db.TenantApiKeys.Add(apiKey);
        db.ApiScopes.Add(new ApiScope { Id = 1, ScopeText = scopeText });
        db.KeyScopeMaps.Add(new KeyScopeMap { Id = 1, ApiKeyUuid = apiKey.KeyId, ScopeId = 1 });
        db.SaveChanges();
    }

    [Fact]
    public async Task Register_WithoutApiKeyHeader_ReturnsBadRequest()
    {
        var (controller, _) = CreateController();

        var result = await controller.Register(null, new RegisterRequest(RoleType), CancellationToken.None);

        var badRequest = result.Result.Should().BeOfType<BadRequestObjectResult>().Subject;
        badRequest.Value.Should().Be($"Missing '{AuthConstants.ApiKeyHeaderName}' header.");
    }

    [Fact]
    public async Task Register_WithoutRoleType_ReturnsBadRequest()
    {
        var (controller, _) = CreateController();

        var result = await controller.Register("thor_ignored", new RegisterRequest(""), CancellationToken.None);

        var badRequest = result.Result.Should().BeOfType<BadRequestObjectResult>().Subject;
        badRequest.Value.Should().Be("RoleType is required.");
    }

    [Fact]
    public async Task Register_WithValidKeyAndGrantedScope_ReturnsOkWithTokens()
    {
        var (controller, factory) = CreateController();
        var (rawKey, apiKey) = IssueApiKey();
        SeedGrantedScope(factory, apiKey, RoleType);

        var result = await controller.Register(rawKey, new RegisterRequest(RoleType), CancellationToken.None);

        var ok = result.Result.Should().BeOfType<OkObjectResult>().Subject;
        var response = ok.Value.Should().BeOfType<TaskAuthResponse>().Subject;
        response.RoleType.Should().Be(RoleType);
        response.Token.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task Register_WithInvalidApiKey_ReturnsUnauthorized()
    {
        var (controller, _) = CreateController();

        var result = await controller.Register("thor_" + Guid.NewGuid() + "_bogus-secret", new RegisterRequest(RoleType), CancellationToken.None);

        result.Result.Should().BeOfType<UnauthorizedResult>();
    }

    [Fact]
    public async Task Register_WithUngrantedRoleType_ReturnsForbiddenWithMessage()
    {
        var (controller, factory) = CreateController();
        var (rawKey, apiKey) = IssueApiKey();
        SeedGrantedScope(factory, apiKey, RoleType);

        var result = await controller.Register(rawKey, new RegisterRequest("uploader"), CancellationToken.None);

        var forbidden = result.Result.Should().BeOfType<ObjectResult>().Subject;
        forbidden.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
        forbidden.Value.Should().Be("Role type 'uploader' is not granted to this API key.");
    }

    [Fact]
    public async Task Refresh_WithoutRefreshToken_ReturnsBadRequest()
    {
        var (controller, _) = CreateController();

        var result = await controller.Refresh(new RefreshRequest(""), CancellationToken.None);

        var badRequest = result.Result.Should().BeOfType<BadRequestObjectResult>().Subject;
        badRequest.Value.Should().Be("RefreshToken is required.");
    }

    [Fact]
    public async Task Refresh_WithValidToken_ReturnsOkWithRotatedTokens()
    {
        var (controller, factory) = CreateController();
        var (rawKey, apiKey) = IssueApiKey();
        SeedGrantedScope(factory, apiKey, RoleType);
        var registerResult = await controller.Register(rawKey, new RegisterRequest(RoleType), CancellationToken.None);
        var initial = (TaskAuthResponse)registerResult.Result.Should().BeOfType<OkObjectResult>().Subject.Value!;

        var result = await controller.Refresh(new RefreshRequest(initial.RefreshToken), CancellationToken.None);

        var ok = result.Result.Should().BeOfType<OkObjectResult>().Subject;
        var response = ok.Value.Should().BeOfType<TaskAuthResponse>().Subject;
        response.RefreshToken.Should().NotBe(initial.RefreshToken);
    }

    [Fact]
    public async Task Refresh_WithInvalidToken_ReturnsUnauthorized()
    {
        var (controller, _) = CreateController();

        var result = await controller.Refresh(new RefreshRequest("not-a-token"), CancellationToken.None);

        result.Result.Should().BeOfType<UnauthorizedResult>();
    }
}

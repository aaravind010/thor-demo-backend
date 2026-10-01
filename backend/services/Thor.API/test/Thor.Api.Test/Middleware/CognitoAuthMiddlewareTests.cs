using System.Text;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Thor.Api.Constants;
using Thor.Api.Middleware;
using Thor.Auth;
using Thor.DataConnectionManager.Exceptions;
using Thor.DataConnectionManager.Routing;
using Thor.DataLayer.Models;

namespace Thor.Api.Test.Middleware;

public class CognitoAuthMiddlewareTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly TenantRouting Route = new()
    {
        TenantId = TenantId, UserPoolId = "us-east-1_pool", AppClientId = "client-1", Region = "us-east-1",
    };

    private readonly ICognitoValidator _cognitoValidator = Substitute.For<ICognitoValidator>();
    private readonly ITokenValidator _thorTokenValidator = Substitute.For<ITokenValidator>();
    private readonly ITenantRoutingResolver _routingResolver = Substitute.For<ITenantRoutingResolver>();
    private bool _nextCalled;

    public CognitoAuthMiddlewareTests()
    {
        _routingResolver.ResolveAsync(TenantId, Arg.Any<CancellationToken>()).Returns(Route);
    }

    /// <summary>
    /// Unsigned compact JWT carrying only an issuer — the middleware peeks <c>iss</c> to pick a
    /// validator, and the (mocked) validator decides validity.
    /// </summary>
    private static string TokenWithIssuer(string issuer)
    {
        static string Segment(string json) =>
            Convert.ToBase64String(Encoding.UTF8.GetBytes(json)).TrimEnd('=').Replace('+', '-').Replace('/', '_');

        return $"{Segment("""{"alg":"none","typ":"JWT"}""")}.{Segment($$"""{"iss":"{{issuer}}"}""")}.sig";
    }

    private static readonly string CognitoToken = TokenWithIssuer("https://cognito-idp.us-east-1.amazonaws.com/us-east-1_pool");
    private static readonly string ThorToken = TokenWithIssuer(TokenIssuers.ThorTaskApi);

    private async Task<int> InvokeAsync(string? authorization, string? tenantHeader)
    {
        var context = new DefaultHttpContext();
        if (authorization is not null)
        {
            context.Request.Headers.Authorization = authorization;
        }

        if (tenantHeader is not null)
        {
            context.Request.Headers[TenantConstants.TenantHeaderName] = tenantHeader;
        }

        var middleware = new CognitoAuthMiddleware(
            _ => { _nextCalled = true; return Task.CompletedTask; },
            _cognitoValidator, _thorTokenValidator, _routingResolver, NullLogger<CognitoAuthMiddleware>.Instance);

        await middleware.InvokeAsync(context);
        return context.Response.StatusCode;
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("ApiKey key-1.secret")]
    [InlineData("bearer lowercase-scheme")]
    public async Task InvokeAsync_MissingOrNonBearerAuthorization_Returns401(string? authorization)
    {
        var status = await InvokeAsync(authorization, TenantId.ToString());

        status.Should().Be(StatusCodes.Status401Unauthorized);
        _nextCalled.Should().BeFalse();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("not-a-guid")]
    public async Task InvokeAsync_MissingOrInvalidTenantHeader_Returns401(string? tenantHeader)
    {
        var status = await InvokeAsync($"Bearer {CognitoToken}", tenantHeader);

        status.Should().Be(StatusCodes.Status401Unauthorized);
        _nextCalled.Should().BeFalse();
    }

    [Fact]
    public async Task InvokeAsync_ValidCognitoTokenForTenantPool_CallsNext()
    {
        _cognitoValidator.ValidateAsync(CognitoToken, Route.UserPoolId, Route.AppClientId, Route.Region)
            .Returns(JwtValidationResult.Success("user-1", []));

        await InvokeAsync($"Bearer {CognitoToken}", TenantId.ToString());

        _nextCalled.Should().BeTrue();
    }

    [Fact]
    public async Task InvokeAsync_CognitoTokenFailingTenantPool_Returns401()
    {
        // e.g. a valid token from another tenant's pool presented with this tenant's id
        _cognitoValidator.ValidateAsync(CognitoToken, Route.UserPoolId, Route.AppClientId, Route.Region)
            .Returns(JwtValidationResult.Failure("signature/claims validation failed"));

        var status = await InvokeAsync($"Bearer {CognitoToken}", TenantId.ToString());

        status.Should().Be(StatusCodes.Status401Unauthorized);
        _nextCalled.Should().BeFalse();
    }

    [Fact]
    public async Task InvokeAsync_UnknownTenant_Returns401()
    {
        var unknownTenant = Guid.NewGuid();
        _routingResolver.ResolveAsync(unknownTenant, Arg.Any<CancellationToken>())
            .ThrowsAsync(new TenantNotFoundException(unknownTenant));

        var status = await InvokeAsync($"Bearer {CognitoToken}", unknownTenant.ToString());

        status.Should().Be(StatusCodes.Status401Unauthorized);
        await _cognitoValidator.DidNotReceiveWithAnyArgs().ValidateAsync(default!, default!, default!, default!);
    }

    [Fact]
    public async Task InvokeAsync_ValidThorTokenForSameTenant_CallsNextWithoutCognito()
    {
        _thorTokenValidator.Validate(ThorToken).Returns(TokenValidationResult.Success(Guid.NewGuid(), TenantId, "scanner"));

        await InvokeAsync($"Bearer {ThorToken}", TenantId.ToString());

        _nextCalled.Should().BeTrue();
        await _cognitoValidator.DidNotReceiveWithAnyArgs().ValidateAsync(default!, default!, default!, default!);
    }

    [Fact]
    public async Task InvokeAsync_ThorTokenForAnotherTenant_Returns401()
    {
        _thorTokenValidator.Validate(ThorToken).Returns(TokenValidationResult.Success(Guid.NewGuid(), Guid.NewGuid(), "scanner"));

        var status = await InvokeAsync($"Bearer {ThorToken}", TenantId.ToString());

        status.Should().Be(StatusCodes.Status401Unauthorized);
        _nextCalled.Should().BeFalse();
    }

    [Fact]
    public async Task InvokeAsync_InvalidThorToken_Returns401()
    {
        _thorTokenValidator.Validate(ThorToken).Returns(TokenValidationResult.Invalid);

        var status = await InvokeAsync($"Bearer {ThorToken}", TenantId.ToString());

        status.Should().Be(StatusCodes.Status401Unauthorized);
        _nextCalled.Should().BeFalse();
    }
}

using System.Text.Json;
using Amazon.CognitoIdentityProvider;
using Amazon.CognitoIdentityProvider.Model;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Thor.Api.Exceptions;
using Thor.Api.Models;
using Thor.Api.Services;
using Thor.DataConnectionManager.Routing;
using Thor.DataLayer.Models;

namespace Thor.Api.Test.Services;

public class IdentityProviderServiceTests
{
    private const string UserPoolId = "us-east-1_tenantPool";
    private const string AppClientId = "tenant-app-client";
    private const string OidcSecret = "super-secret-oidc-value";

    private static readonly Guid TenantId = Guid.NewGuid();

    private readonly IAmazonCognitoIdentityProvider _cognito = Substitute.For<IAmazonCognitoIdentityProvider>();
    private readonly ITenantRoutingResolver _routingResolver = Substitute.For<ITenantRoutingResolver>();

    public IdentityProviderServiceTests()
    {
        _routingResolver.ResolveAsync(TenantId, Arg.Any<CancellationToken>())
            .Returns(new TenantRouting { TenantId = TenantId, UserPoolId = UserPoolId, AppClientId = AppClientId });
    }

    private IdentityProviderService CreateService() =>
        new(_cognito, _routingResolver, NullLogger<IdentityProviderService>.Instance);

    private void AppClientHasProviders(List<string>? providers) =>
        _cognito.DescribeUserPoolClientAsync(
                Arg.Is<DescribeUserPoolClientRequest>(r => r.UserPoolId == UserPoolId && r.ClientId == AppClientId),
                Arg.Any<CancellationToken>())
            .Returns(new DescribeUserPoolClientResponse
            {
                UserPoolClient = new UserPoolClientType
                {
                    UserPoolId = UserPoolId,
                    ClientId = AppClientId,
                    ClientName = "thor-acme-app",
                    SupportedIdentityProviders = providers,
                },
            });

    private void ProviderExists(string name, IdentityProviderTypeType type, Dictionary<string, string>? details = null) =>
        _cognito.DescribeIdentityProviderAsync(
                Arg.Is<DescribeIdentityProviderRequest>(r => r.UserPoolId == UserPoolId && r.ProviderName == name),
                Arg.Any<CancellationToken>())
            .Returns(new DescribeIdentityProviderResponse
            {
                IdentityProvider = new IdentityProviderType { ProviderName = name, ProviderType = type, ProviderDetails = details },
            });

    private void CreateReturnsInput() =>
        _cognito.CreateIdentityProviderAsync(Arg.Any<CreateIdentityProviderRequest>(), Arg.Any<CancellationToken>())
            .Returns(ci =>
            {
                var r = ci.Arg<CreateIdentityProviderRequest>();
                return new CreateIdentityProviderResponse
                {
                    IdentityProvider = new IdentityProviderType
                    {
                        ProviderName = r.ProviderName,
                        ProviderType = r.ProviderType,
                        ProviderDetails = r.ProviderDetails,
                        AttributeMapping = r.AttributeMapping,
                    },
                };
            });

    private static CreateTenantIdentityProviderRequest SamlRequest(string name = "Okta") =>
        new(name, "SAML", new SamlProviderSettings("https://idp.example.com/metadata", null, true), null,
            new Dictionary<string, string> { ["email"] = "http://schemas.xmlsoap.org/ws/2005/05/identity/claims/emailaddress" },
            null);

    private static CreateTenantIdentityProviderRequest OidcRequest(string name = "Entra") =>
        new(name, "OIDC", null,
            new OidcProviderSettings("oidc-client", OidcSecret, "https://login.example.com/v2.0", "openid email", null),
            null, null);

    private static UpdateTenantIdentityProviderRequest OidcUpdate() =>
        new("OIDC", null,
            new OidcProviderSettings("oidc-client", OidcSecret, "https://login.example.com/v2.0", "openid email profile", "POST"),
            null, null);

    [Fact]
    public async Task CreateAsync_Saml_CreatesOnTenantPoolAndEnablesOnAppClient()
    {
        AppClientHasProviders(["COGNITO"]);
        CreateReturnsInput();

        var response = await CreateService().CreateAsync(TenantId, SamlRequest(), CancellationToken.None);

        await _cognito.Received(1).CreateIdentityProviderAsync(
            Arg.Is<CreateIdentityProviderRequest>(r =>
                r.UserPoolId == UserPoolId &&
                r.ProviderName == "Okta" &&
                r.ProviderType == IdentityProviderTypeType.SAML &&
                r.ProviderDetails["MetadataURL"] == "https://idp.example.com/metadata" &&
                r.ProviderDetails["IDPSignout"] == "true"),
            Arg.Any<CancellationToken>());

        await _cognito.Received(1).UpdateUserPoolClientAsync(
            Arg.Is<UpdateUserPoolClientRequest>(r =>
                r.UserPoolId == UserPoolId &&
                r.ClientId == AppClientId &&
                r.ClientName == "thor-acme-app" &&
                r.SupportedIdentityProviders.SequenceEqual(new[] { "COGNITO", "Okta" })),
            Arg.Any<CancellationToken>());

        response.ProviderName.Should().Be("Okta");
        response.Saml!.MetadataUrl.Should().Be("https://idp.example.com/metadata");
        response.Saml.IdpSignout.Should().BeTrue();
        response.Oidc.Should().BeNull();
    }

    [Fact]
    public async Task CreateAsync_AppClientWithNoProvidersListed_AlwaysAddsCognito()
    {
        AppClientHasProviders(null); // how tenant provisioning creates the app client today
        CreateReturnsInput();

        await CreateService().CreateAsync(TenantId, SamlRequest(), CancellationToken.None);

        await _cognito.Received(1).UpdateUserPoolClientAsync(
            Arg.Is<UpdateUserPoolClientRequest>(r => r.SupportedIdentityProviders.SequenceEqual(new[] { "COGNITO", "Okta" })),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CreateAsync_Oidc_PassesSecretToCognitoButNeverReturnsIt()
    {
        AppClientHasProviders(["COGNITO"]);
        CreateReturnsInput(); // Cognito echoes client_secret back in ProviderDetails

        var response = await CreateService().CreateAsync(TenantId, OidcRequest(), CancellationToken.None);

        await _cognito.Received(1).CreateIdentityProviderAsync(
            Arg.Is<CreateIdentityProviderRequest>(r =>
                r.ProviderType == IdentityProviderTypeType.OIDC &&
                r.ProviderDetails["client_secret"] == OidcSecret &&
                r.ProviderDetails["attributes_request_method"] == "GET"),
            Arg.Any<CancellationToken>());

        response.Oidc!.ClientId.Should().Be("oidc-client");
        JsonSerializer.Serialize(response).Should().NotContain(OidcSecret);
    }

    [Fact]
    public async Task GetAsync_Oidc_NeverReturnsSecret()
    {
        ProviderExists("Entra", IdentityProviderTypeType.OIDC, new Dictionary<string, string>
        {
            ["client_id"] = "oidc-client",
            ["client_secret"] = OidcSecret,
            ["oidc_issuer"] = "https://login.example.com/v2.0",
            ["authorize_scopes"] = "openid email",
            ["attributes_request_method"] = "GET",
        });

        var response = await CreateService().GetAsync(TenantId, "Entra", CancellationToken.None);

        response.Oidc!.Issuer.Should().Be("https://login.example.com/v2.0");
        JsonSerializer.Serialize(response).Should().NotContain(OidcSecret);
    }

    [Fact]
    public async Task CreateAsync_WhenAppClientUpdateFails_DeletesTheNewProviderAndRethrows()
    {
        AppClientHasProviders(["COGNITO"]);
        CreateReturnsInput();
        _cognito.UpdateUserPoolClientAsync(Arg.Any<UpdateUserPoolClientRequest>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new TooManyRequestsException("throttled"));

        var act = () => CreateService().CreateAsync(TenantId, SamlRequest(), CancellationToken.None);

        await act.Should().ThrowAsync<TooManyRequestsException>();
        await _cognito.Received(1).DeleteIdentityProviderAsync(
            Arg.Is<DeleteIdentityProviderRequest>(r => r.UserPoolId == UserPoolId && r.ProviderName == "Okta"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CreateAsync_DuplicateName_ThrowsConflict()
    {
        _cognito.CreateIdentityProviderAsync(Arg.Any<CreateIdentityProviderRequest>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new DuplicateProviderException("exists"));

        var act = () => CreateService().CreateAsync(TenantId, SamlRequest(), CancellationToken.None);

        await act.Should().ThrowAsync<IdentityProviderConflictException>();
        await _cognito.DidNotReceiveWithAnyArgs().UpdateUserPoolClientAsync(default!, default);
    }

    [Fact]
    public async Task CreateAsync_CognitoRejectsParameters_ThrowsInvalid()
    {
        _cognito.CreateIdentityProviderAsync(Arg.Any<CreateIdentityProviderRequest>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidParameterException("metadata unreachable"));

        var act = () => CreateService().CreateAsync(TenantId, SamlRequest(), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidIdentityProviderException>().WithMessage("metadata unreachable");
    }

    public static TheoryData<CreateTenantIdentityProviderRequest> InvalidCreateRequests() => new()
    {
        SamlRequest("COGNITO"),
        SamlRequest("cognito"),
        SamlRequest(" "),
        SamlRequest() with { ProviderType = "Google" },
        SamlRequest() with { Saml = null },
        SamlRequest() with { Saml = new SamlProviderSettings("https://idp/metadata", "<xml/>", null) },
        SamlRequest() with { Saml = new SamlProviderSettings(null, null, null) },
        SamlRequest() with { Oidc = OidcRequest().Oidc },
        OidcRequest() with { Oidc = OidcRequest().Oidc! with { ClientSecret = "" } },
        OidcRequest() with { Oidc = null },
    };

    [Theory]
    [MemberData(nameof(InvalidCreateRequests))]
    public async Task CreateAsync_InvalidRequest_ThrowsInvalidWithoutCallingCognito(CreateTenantIdentityProviderRequest request)
    {
        var act = () => CreateService().CreateAsync(TenantId, request, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidIdentityProviderException>();
        _cognito.ReceivedCalls().Should().BeEmpty();
    }

    [Fact]
    public async Task UpdateAsync_ReplacesSettingsOnTenantPool()
    {
        ProviderExists("Entra", IdentityProviderTypeType.OIDC);
        _cognito.UpdateIdentityProviderAsync(Arg.Any<UpdateIdentityProviderRequest>(), Arg.Any<CancellationToken>())
            .Returns(new UpdateIdentityProviderResponse
            {
                IdentityProvider = new IdentityProviderType { ProviderName = "Entra", ProviderType = IdentityProviderTypeType.OIDC },
            });

        await CreateService().UpdateAsync(TenantId, "Entra", OidcUpdate(), CancellationToken.None);

        await _cognito.Received(1).UpdateIdentityProviderAsync(
            Arg.Is<UpdateIdentityProviderRequest>(r =>
                r.UserPoolId == UserPoolId &&
                r.ProviderName == "Entra" &&
                r.ProviderDetails["authorize_scopes"] == "openid email profile" &&
                r.ProviderDetails["attributes_request_method"] == "POST"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UpdateAsync_ChangingProviderType_ThrowsInvalid()
    {
        ProviderExists("Entra", IdentityProviderTypeType.SAML);

        var act = () => CreateService().UpdateAsync(TenantId, "Entra", OidcUpdate(), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidIdentityProviderException>();
        await _cognito.DidNotReceiveWithAnyArgs().UpdateIdentityProviderAsync(default!, default);
    }

    [Fact]
    public async Task UpdateAsync_UnknownProvider_ThrowsNotFound()
    {
        _cognito.DescribeIdentityProviderAsync(Arg.Any<DescribeIdentityProviderRequest>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new ResourceNotFoundException("missing"));

        var act = () => CreateService().UpdateAsync(TenantId, "Entra", OidcUpdate(), CancellationToken.None);

        await act.Should().ThrowAsync<IdentityProviderNotFoundException>();
    }

    [Fact]
    public async Task DeleteAsync_RemovesFromAppClientKeepingOthersThenDeletes()
    {
        ProviderExists("Okta", IdentityProviderTypeType.SAML);
        AppClientHasProviders(["COGNITO", "Okta", "Entra"]);

        await CreateService().DeleteAsync(TenantId, "Okta", CancellationToken.None);

        Received.InOrder(() =>
        {
            _cognito.UpdateUserPoolClientAsync(
                Arg.Is<UpdateUserPoolClientRequest>(r => r.SupportedIdentityProviders.SequenceEqual(new[] { "COGNITO", "Entra" })),
                Arg.Any<CancellationToken>());
            _cognito.DeleteIdentityProviderAsync(
                Arg.Is<DeleteIdentityProviderRequest>(r => r.UserPoolId == UserPoolId && r.ProviderName == "Okta"),
                Arg.Any<CancellationToken>());
        });
    }

    [Fact]
    public async Task DeleteAsync_UnknownProvider_ThrowsNotFoundWithoutTouchingAppClient()
    {
        _cognito.DescribeIdentityProviderAsync(Arg.Any<DescribeIdentityProviderRequest>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new ResourceNotFoundException("missing"));

        var act = () => CreateService().DeleteAsync(TenantId, "Okta", CancellationToken.None);

        await act.Should().ThrowAsync<IdentityProviderNotFoundException>();
        await _cognito.DidNotReceiveWithAnyArgs().UpdateUserPoolClientAsync(default!, default);
        await _cognito.DidNotReceiveWithAnyArgs().DeleteIdentityProviderAsync(default!, default);
    }

    [Theory]
    [InlineData("COGNITO")]
    [InlineData("Cognito")]
    public async Task DeleteAsync_CognitoProvider_IsRejected(string providerName)
    {
        var act = () => CreateService().DeleteAsync(TenantId, providerName, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidIdentityProviderException>();
        await _cognito.DidNotReceiveWithAnyArgs().UpdateUserPoolClientAsync(default!, default);
        await _cognito.DidNotReceiveWithAnyArgs().DeleteIdentityProviderAsync(default!, default);
    }

    [Fact]
    public async Task ListAsync_FollowsPaginationOnTenantPool()
    {
        _cognito.ListIdentityProvidersAsync(
                Arg.Is<ListIdentityProvidersRequest>(r => r.UserPoolId == UserPoolId && r.NextToken == null),
                Arg.Any<CancellationToken>())
            .Returns(new ListIdentityProvidersResponse
            {
                Providers = [new ProviderDescription { ProviderName = "Okta", ProviderType = IdentityProviderTypeType.SAML }],
                NextToken = "page-2",
            });
        _cognito.ListIdentityProvidersAsync(
                Arg.Is<ListIdentityProvidersRequest>(r => r.UserPoolId == UserPoolId && r.NextToken == "page-2"),
                Arg.Any<CancellationToken>())
            .Returns(new ListIdentityProvidersResponse
            {
                Providers = [new ProviderDescription { ProviderName = "Entra", ProviderType = IdentityProviderTypeType.OIDC }],
            });

        var providers = await CreateService().ListAsync(TenantId, CancellationToken.None);

        providers.Select(p => (p.ProviderName, p.ProviderType))
            .Should().Equal(("Okta", "SAML"), ("Entra", "OIDC"));
    }
}

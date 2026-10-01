using Amazon.CognitoIdentityProvider;
using Amazon.CognitoIdentityProvider.Model;
using FluentAssertions;
using Thor.Api.Services;

namespace Thor.Api.Test.Services;

public class UserPoolClientUpdateFactoryTests
{
    // Every field populated with a non-default value, so a field the factory forgets to copy shows
    // up as a mismatch rather than as null == null.
    private static UserPoolClientType FullyPopulatedClient() => new()
    {
        UserPoolId = "us-east-1_pool",
        ClientId = "client-1",
        ClientName = "thor-acme-app",
        ClientSecret = "not-copied",
        AccessTokenValidity = 5,
        IdTokenValidity = 6,
        RefreshTokenValidity = 7,
        AuthSessionValidity = 8,
        AllowedOAuthFlows = ["code"],
        AllowedOAuthFlowsUserPoolClient = true,
        AllowedOAuthScopes = ["openid"],
        AnalyticsConfiguration = new AnalyticsConfigurationType { ApplicationId = "app-1" },
        CallbackURLs = ["https://acme.example.com/callback"],
        LogoutURLs = ["https://acme.example.com/logout"],
        DefaultRedirectURI = "https://acme.example.com/callback",
        EnablePropagateAdditionalUserContextData = true,
        EnableTokenRevocation = true,
        ExplicitAuthFlows = ["ALLOW_USER_SRP_AUTH"],
        PreventUserExistenceErrors = PreventUserExistenceErrorTypes.ENABLED,
        ReadAttributes = ["email"],
        WriteAttributes = ["email"],
        RefreshTokenRotation = new RefreshTokenRotationType { RetryGracePeriodSeconds = 10 },
        TokenValidityUnits = new TokenValidityUnitsType { AccessToken = TimeUnitsType.Hours },
        SupportedIdentityProviders = ["COGNITO"],
        CreationDate = DateTime.UtcNow,
        LastModifiedDate = DateTime.UtcNow,
    };

    [Fact]
    public void WithSupportedIdentityProviders_CopiesEveryOtherUpdatableField()
    {
        var client = FullyPopulatedClient();
        var providers = new List<string> { "COGNITO", "Okta" };

        var request = UserPoolClientUpdateFactory.WithSupportedIdentityProviders(client, providers);

        request.SupportedIdentityProviders.Should().BeSameAs(providers);

        var copiedFields = typeof(UpdateUserPoolClientRequest).GetProperties()
            .Where(p => p.CanWrite && p.Name != nameof(UpdateUserPoolClientRequest.SupportedIdentityProviders));

        foreach (var field in copiedFields)
        {
            var source = typeof(UserPoolClientType).GetProperty(field.Name);
            source.Should().NotBeNull($"UpdateUserPoolClientRequest.{field.Name} has no source on UserPoolClientType");

            var expected = source!.GetValue(client);
            expected.Should().NotBeNull($"the fixture must populate {field.Name} so its copy is checked");
            field.GetValue(request).Should().Be(expected, $"{field.Name} would otherwise be reset by UpdateUserPoolClient");
        }
    }
}

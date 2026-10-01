using Amazon.CognitoIdentityProvider.Model;

namespace Thor.Api.Services;

/// <summary>
/// Builds an <see cref="UpdateUserPoolClientRequest"/> that changes only SupportedIdentityProviders.
/// Cognito's UpdateUserPoolClient resets every omitted setting to its default (token validity, auth
/// flows, OAuth settings, ...), so every other field is copied from the client's current settings.
/// UserPoolClientUpdateFactoryTests fails if an SDK upgrade adds a field this doesn't copy.
/// </summary>
public static class UserPoolClientUpdateFactory
{
    public static UpdateUserPoolClientRequest WithSupportedIdentityProviders(
        UserPoolClientType client, List<string> supportedIdentityProviders) => new()
    {
        UserPoolId = client.UserPoolId,
        ClientId = client.ClientId,
        SupportedIdentityProviders = supportedIdentityProviders,

        AccessTokenValidity = client.AccessTokenValidity,
        AllowedOAuthFlows = client.AllowedOAuthFlows,
        AllowedOAuthFlowsUserPoolClient = client.AllowedOAuthFlowsUserPoolClient,
        AllowedOAuthScopes = client.AllowedOAuthScopes,
        AnalyticsConfiguration = client.AnalyticsConfiguration,
        AuthSessionValidity = client.AuthSessionValidity,
        CallbackURLs = client.CallbackURLs,
        ClientName = client.ClientName,
        DefaultRedirectURI = client.DefaultRedirectURI,
        EnablePropagateAdditionalUserContextData = client.EnablePropagateAdditionalUserContextData,
        EnableTokenRevocation = client.EnableTokenRevocation,
        ExplicitAuthFlows = client.ExplicitAuthFlows,
        IdTokenValidity = client.IdTokenValidity,
        LogoutURLs = client.LogoutURLs,
        PreventUserExistenceErrors = client.PreventUserExistenceErrors,
        ReadAttributes = client.ReadAttributes,
        RefreshTokenRotation = client.RefreshTokenRotation,
        RefreshTokenValidity = client.RefreshTokenValidity,
        TokenValidityUnits = client.TokenValidityUnits,
        WriteAttributes = client.WriteAttributes,
    };
}

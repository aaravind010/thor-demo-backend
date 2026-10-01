using Amazon.CognitoIdentityProvider;
using Amazon.CognitoIdentityProvider.Model;
using Thor.Api.Exceptions;
using Thor.Api.Models;
using Thor.DataConnectionManager.Routing;
using Thor.DataLayer.Models;

namespace Thor.Api.Services;

/// <summary>
/// Backs <c>v{version}/identity-providers</c>: manages SAML/OIDC identity providers on the tenant's
/// own Cognito user pool and enables them on its app client. The pool and app client come only
/// from the verified tenant's tenant_routing row, never from the request.
/// <para>
/// Provider settings are passed straight to Cognito and never stored by Thor. The OIDC client
/// secret is never logged or returned: responses are built from an allow-list of non-secret keys.
/// Provider names can't be changed — Cognito keys federated users by
/// <c>{ProviderName}_{IdP subject}</c>, so a new name means new users.
/// </para>
/// </summary>
public sealed class IdentityProviderService(
    IAmazonCognitoIdentityProvider cognito,
    ITenantRoutingResolver tenantRoutingResolver,
    ILogger<IdentityProviderService> logger)
{
    /// <summary>
    /// Cognito's local-account provider. Always kept on the app client, and never manageable
    /// through this API, so a misconfigured IdP can't lock the tenant's local admins out.
    /// </summary>
    public const string CognitoProvider = "COGNITO";

    private const int ListPageSize = 60;

    public async Task<IReadOnlyList<TenantIdentityProviderSummaryResponse>> ListAsync(
        Guid tenantId, CancellationToken cancellationToken)
    {
        var routing = await tenantRoutingResolver.ResolveAsync(tenantId, cancellationToken);

        var providers = new List<TenantIdentityProviderSummaryResponse>();
        string? nextToken = null;
        do
        {
            var page = await cognito.ListIdentityProvidersAsync(
                new ListIdentityProvidersRequest
                {
                    UserPoolId = routing.UserPoolId,
                    MaxResults = ListPageSize,
                    NextToken = nextToken,
                },
                cancellationToken);

            foreach (var provider in page.Providers ?? [])
            {
                providers.Add(new TenantIdentityProviderSummaryResponse(
                    provider.ProviderName, provider.ProviderType?.Value ?? string.Empty,
                    provider.CreationDate, provider.LastModifiedDate));
            }

            nextToken = page.NextToken;
        }
        while (!string.IsNullOrEmpty(nextToken));

        return providers;
    }

    public async Task<TenantIdentityProviderResponse> GetAsync(
        Guid tenantId, string providerName, CancellationToken cancellationToken)
    {
        var routing = await tenantRoutingResolver.ResolveAsync(tenantId, cancellationToken);
        var provider = await DescribeAsync(routing.UserPoolId, providerName, cancellationToken);
        return ToResponse(provider);
    }

    public async Task<TenantIdentityProviderResponse> CreateAsync(
        Guid tenantId, CreateTenantIdentityProviderRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.ProviderName))
        {
            throw new InvalidIdentityProviderException("ProviderName is required.");
        }

        EnsureNotReserved(request.ProviderName);
        var (providerType, providerDetails) = BuildProviderDetails(request.ProviderType, request.Saml, request.Oidc);

        var routing = await tenantRoutingResolver.ResolveAsync(tenantId, cancellationToken);

        IdentityProviderType created;
        try
        {
            var response = await cognito.CreateIdentityProviderAsync(
                new CreateIdentityProviderRequest
                {
                    UserPoolId = routing.UserPoolId,
                    ProviderName = request.ProviderName,
                    ProviderType = providerType,
                    ProviderDetails = providerDetails,
                    AttributeMapping = request.AttributeMapping,
                    IdpIdentifiers = request.IdpIdentifiers,
                },
                cancellationToken);
            created = response.IdentityProvider;
        }
        catch (DuplicateProviderException)
        {
            throw new IdentityProviderConflictException(request.ProviderName);
        }
        catch (InvalidParameterException ex)
        {
            throw new InvalidIdentityProviderException(ex.Message, ex);
        }

        try
        {
            await SetEnabledOnAppClientAsync(routing, request.ProviderName, enabled: true, cancellationToken);
        }
        catch (Exception ex)
        {
            // An IdP missing from the app client can't be signed in with, and leaving it behind
            // would turn the caller's retry into a 409 — so undo the create.
            logger.LogError(ex, "Enabling identity provider {ProviderName} on the app client failed; deleting it", request.ProviderName);
            await DeleteProviderBestEffortAsync(routing.UserPoolId, request.ProviderName);
            throw;
        }

        logger.LogInformation(
            "Created {ProviderType} identity provider {ProviderName} for tenant {TenantId}",
            providerType.Value, request.ProviderName, tenantId);

        return ToResponse(created);
    }

    public async Task<TenantIdentityProviderResponse> UpdateAsync(
        Guid tenantId, string providerName, UpdateTenantIdentityProviderRequest request, CancellationToken cancellationToken)
    {
        var (providerType, providerDetails) = BuildProviderDetails(request.ProviderType, request.Saml, request.Oidc);

        var routing = await tenantRoutingResolver.ResolveAsync(tenantId, cancellationToken);
        var existing = await DescribeAsync(routing.UserPoolId, providerName, cancellationToken);

        if (existing.ProviderType != providerType)
        {
            throw new InvalidIdentityProviderException(
                $"ProviderType can't be changed (provider '{providerName}' is {existing.ProviderType?.Value}).");
        }

        IdentityProviderType updated;
        try
        {
            var response = await cognito.UpdateIdentityProviderAsync(
                new UpdateIdentityProviderRequest
                {
                    UserPoolId = routing.UserPoolId,
                    ProviderName = providerName,
                    ProviderDetails = providerDetails,
                    AttributeMapping = request.AttributeMapping,
                    IdpIdentifiers = request.IdpIdentifiers,
                },
                cancellationToken);
            updated = response.IdentityProvider;
        }
        catch (ResourceNotFoundException)
        {
            throw new IdentityProviderNotFoundException(providerName);
        }
        catch (InvalidParameterException ex)
        {
            throw new InvalidIdentityProviderException(ex.Message, ex);
        }

        logger.LogInformation(
            "Updated {ProviderType} identity provider {ProviderName} for tenant {TenantId}",
            providerType.Value, providerName, tenantId);

        return ToResponse(updated);
    }

    public async Task DeleteAsync(Guid tenantId, string providerName, CancellationToken cancellationToken)
    {
        var routing = await tenantRoutingResolver.ResolveAsync(tenantId, cancellationToken);

        // 404 before touching the app client.
        _ = await DescribeAsync(routing.UserPoolId, providerName, cancellationToken);

        // App client first, so it never lists a provider that no longer exists. Removal is a
        // no-op on retry if the delete below then fails.
        await SetEnabledOnAppClientAsync(routing, providerName, enabled: false, cancellationToken);

        try
        {
            await cognito.DeleteIdentityProviderAsync(
                new DeleteIdentityProviderRequest { UserPoolId = routing.UserPoolId, ProviderName = providerName },
                cancellationToken);
        }
        catch (ResourceNotFoundException)
        {
            throw new IdentityProviderNotFoundException(providerName);
        }

        logger.LogInformation(
            "Deleted identity provider {ProviderName} for tenant {TenantId}", providerName, tenantId);
    }

    private async Task<IdentityProviderType> DescribeAsync(
        string userPoolId, string providerName, CancellationToken cancellationToken)
    {
        EnsureNotReserved(providerName);

        try
        {
            var response = await cognito.DescribeIdentityProviderAsync(
                new DescribeIdentityProviderRequest { UserPoolId = userPoolId, ProviderName = providerName },
                cancellationToken);
            return response.IdentityProvider;
        }
        catch (ResourceNotFoundException)
        {
            throw new IdentityProviderNotFoundException(providerName);
        }
        catch (InvalidParameterException ex)
        {
            throw new InvalidIdentityProviderException(ex.Message, ex);
        }
    }

    private async Task SetEnabledOnAppClientAsync(
        TenantRouting routing, string providerName, bool enabled, CancellationToken cancellationToken)
    {
        var described = await cognito.DescribeUserPoolClientAsync(
            new DescribeUserPoolClientRequest { UserPoolId = routing.UserPoolId, ClientId = routing.AppClientId },
            cancellationToken);
        var client = described.UserPoolClient;

        var current = client.SupportedIdentityProviders ?? [];
        var providers = current.Where(p => !string.Equals(p, providerName, StringComparison.Ordinal)).ToList();
        if (!providers.Contains(CognitoProvider, StringComparer.Ordinal))
        {
            providers.Insert(0, CognitoProvider);
        }

        if (enabled)
        {
            providers.Add(providerName);
        }

        if (providers.SequenceEqual(current, StringComparer.Ordinal))
        {
            return;
        }

        await cognito.UpdateUserPoolClientAsync(
            UserPoolClientUpdateFactory.WithSupportedIdentityProviders(client, providers), cancellationToken);
    }

    private async Task DeleteProviderBestEffortAsync(string userPoolId, string providerName)
    {
        try
        {
            // Not the request's token: the request may already be cancelled, and this must still run.
            await cognito.DeleteIdentityProviderAsync(
                new DeleteIdentityProviderRequest { UserPoolId = userPoolId, ProviderName = providerName },
                CancellationToken.None);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Rolling back identity provider {ProviderName} failed; it must be deleted manually", providerName);
        }
    }

    private static void EnsureNotReserved(string providerName)
    {
        if (string.Equals(providerName, CognitoProvider, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidIdentityProviderException($"'{CognitoProvider}' is reserved for local accounts.");
        }
    }

    private static (IdentityProviderTypeType Type, Dictionary<string, string> Details) BuildProviderDetails(
        string? providerType, SamlProviderSettings? saml, OidcProviderSettings? oidc)
    {
        if (string.Equals(providerType, IdentityProviderTypeType.SAML.Value, StringComparison.OrdinalIgnoreCase))
        {
            if (saml is null || oidc is not null)
            {
                throw new InvalidIdentityProviderException("SAML providers need a 'saml' block and no 'oidc' block.");
            }

            var hasUrl = !string.IsNullOrWhiteSpace(saml.MetadataUrl);
            var hasFile = !string.IsNullOrWhiteSpace(saml.MetadataFile);
            if (hasUrl == hasFile)
            {
                throw new InvalidIdentityProviderException("Exactly one of saml.metadataUrl or saml.metadataFile is required.");
            }

            var details = hasUrl
                ? new Dictionary<string, string> { ["MetadataURL"] = saml.MetadataUrl! }
                : new Dictionary<string, string> { ["MetadataFile"] = saml.MetadataFile! };

            if (saml.IdpSignout is { } idpSignout)
            {
                details["IDPSignout"] = idpSignout ? "true" : "false";
            }

            return (IdentityProviderTypeType.SAML, details);
        }

        if (string.Equals(providerType, IdentityProviderTypeType.OIDC.Value, StringComparison.OrdinalIgnoreCase))
        {
            if (oidc is null || saml is not null)
            {
                throw new InvalidIdentityProviderException("OIDC providers need an 'oidc' block and no 'saml' block.");
            }

            if (string.IsNullOrWhiteSpace(oidc.ClientId) || string.IsNullOrWhiteSpace(oidc.ClientSecret) ||
                string.IsNullOrWhiteSpace(oidc.Issuer) || string.IsNullOrWhiteSpace(oidc.AuthorizeScopes))
            {
                throw new InvalidIdentityProviderException(
                    "oidc.clientId, oidc.clientSecret, oidc.issuer and oidc.authorizeScopes are required.");
            }

            return (IdentityProviderTypeType.OIDC, new Dictionary<string, string>
            {
                ["client_id"] = oidc.ClientId,
                ["client_secret"] = oidc.ClientSecret,
                ["oidc_issuer"] = oidc.Issuer,
                ["authorize_scopes"] = oidc.AuthorizeScopes,
                ["attributes_request_method"] = string.IsNullOrWhiteSpace(oidc.AttributesRequestMethod)
                    ? "GET"
                    : oidc.AttributesRequestMethod,
            });
        }

        throw new InvalidIdentityProviderException("ProviderType must be 'SAML' or 'OIDC'.");
    }

    private static TenantIdentityProviderResponse ToResponse(IdentityProviderType provider)
    {
        var details = provider.ProviderDetails ?? new Dictionary<string, string>();

        var saml = provider.ProviderType == IdentityProviderTypeType.SAML
            ? new SamlProviderSettingsResponse(
                details.GetValueOrDefault("MetadataURL"),
                bool.TryParse(details.GetValueOrDefault("IDPSignout"), out var idpSignout) ? idpSignout : null)
            : null;

        var oidc = provider.ProviderType == IdentityProviderTypeType.OIDC
            ? new OidcProviderSettingsResponse(
                details.GetValueOrDefault("client_id"),
                details.GetValueOrDefault("oidc_issuer"),
                details.GetValueOrDefault("authorize_scopes"),
                details.GetValueOrDefault("attributes_request_method"))
            : null;

        return new TenantIdentityProviderResponse(
            provider.ProviderName,
            provider.ProviderType?.Value ?? string.Empty,
            provider.CreationDate,
            provider.LastModifiedDate,
            saml,
            oidc,
            provider.AttributeMapping ?? new Dictionary<string, string>(),
            provider.IdpIdentifiers ?? []);
    }
}

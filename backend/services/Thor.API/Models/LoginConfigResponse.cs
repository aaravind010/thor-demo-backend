namespace Thor.Api.Models;

/// <summary>
/// The Cognito pool/app client a tenant's users sign in to. All public values: the SPA's app
/// client has no secret, and these ids appear in every token the pool issues anyway.
/// </summary>
public sealed record LoginConfigResponse(string UserPoolId, string AppClientId, string Region);

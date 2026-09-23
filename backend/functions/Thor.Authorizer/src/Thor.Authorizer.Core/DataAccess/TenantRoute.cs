namespace Thor.Authorizer.Core.DataAccess;

public sealed record TenantRoute(
    string TenantId,
    string Subdomain,
    string UserPoolId,
    string AppClientId,
    string Region
);

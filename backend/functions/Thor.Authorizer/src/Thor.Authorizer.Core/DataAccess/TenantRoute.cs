namespace Thor.Authorizer.Core.DataAccess;

public sealed record TenantRoute(
    string TenantId,
    string UserPoolId,
    string AppClientId,
    string Region
);

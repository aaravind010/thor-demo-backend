using Thor.Authorizer.Core.DataAccess;

namespace Thor.Authorizer.Core.Tenancy;

public sealed record TenantResolutionResult(bool IsResolved, TenantRoute? Route);

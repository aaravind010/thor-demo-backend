using Thor.Authorizer.Core.DataAccess;

namespace Thor.Authorizer.Test.TestFixtures;

public static class TenantRouteFixtures
{
    public static TenantRoute Default() => new(
        TenantId: "tenant-1",
        Subdomain: "acme",
        UserPoolId: "us-east-1_ExamplePool",
        AppClientId: "client-abc123",
        Region: "us-east-1");

    public static TenantRoute WithSubdomain(string subdomain) => Default() with { Subdomain = subdomain };
}

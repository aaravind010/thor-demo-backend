using Thor.Authorizer.Core.DataAccess;

namespace Thor.Authorizer.Test.TestFixtures;

public static class TenantRouteFixtures
{
    public static TenantRoute Default() => new(
        TenantId: "tenant-1",
        UserPoolId: "us-east-1_ExamplePool",
        AppClientId: "client-abc123",
        Region: "us-east-1");

    public static string CognitoIssuerFor(TenantRoute route) =>
        $"https://cognito-idp.{route.Region}.amazonaws.com/{route.UserPoolId}";
}

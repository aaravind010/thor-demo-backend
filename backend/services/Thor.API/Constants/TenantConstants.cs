namespace Thor.Api.Constants;

/// <summary>Trusted headers set by the Lambda authorizer for tenant-scoped endpoints.</summary>
public static class TenantConstants
{
    public const string TenantHeaderName = "X-THOR-TENANT-ID";

    public const string ActorHeaderName = "X-THOR-ACTOR-ID";
}

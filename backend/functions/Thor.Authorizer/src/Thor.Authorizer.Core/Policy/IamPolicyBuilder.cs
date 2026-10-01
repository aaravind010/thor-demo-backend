namespace Thor.Authorizer.Core.Policy;

public sealed class IamPolicyBuilder : IPolicyBuilder
{
    public PolicyDocument Build(AuthResult authResult, string methodArn)
    {
        var resource = ToApiWideResource(methodArn);

        if (!authResult.IsAllowed)
        {
            // Deny: minimal context, no principal-identifying info leaked into a denied response.
            return new PolicyDocument(
                PrincipalId: "unauthorized",
                Effect: "Deny",
                Resource: resource,
                Context: new Dictionary<string, string>());
        }

        var context = new Dictionary<string, string>
        {
            [AuthorizerContextKeys.TenantId] = authResult.TenantId,
            [AuthorizerContextKeys.CallerType] = authResult.CallerType,
            [AuthorizerContextKeys.PrincipalId] = authResult.PrincipalId,
            [AuthorizerContextKeys.Scopes] = string.Join(',', authResult.Scopes),
            [AuthorizerContextKeys.Groups] = string.Join(',', authResult.Groups),
        };

        return new PolicyDocument(
            PrincipalId: authResult.PrincipalId,
            Effect: "Allow",
            Resource: resource,
            Context: context);
    }

    /// <summary>
    /// Widens "arn:aws:execute-api:{region}:{account}:{api-id}/{stage}/{method}/{path}" down to
    /// "arn:aws:execute-api:{region}:{account}:{api-id}/{stage}/*". API Gateway caches this
    /// authorizer's response keyed on its identity sources (see authorizer.tf) — a route-specific
    /// Resource would produce a false Deny on any cache hit for a route the policy doesn't cover.
    /// Route-level authorization isn't done via this policy today (see AuthorizerHandler); it
    /// belongs in the downstream service, keyed off the Scopes/CallerType context above.
    /// </summary>
    private static string ToApiWideResource(string methodArn)
    {
        var lastColon = methodArn.LastIndexOf(':');
        if (lastColon < 0)
        {
            return methodArn;
        }

        var segments = methodArn[(lastColon + 1)..].Split('/');
        if (segments.Length < 2)
        {
            return methodArn;
        }

        return $"{methodArn[..(lastColon + 1)]}{segments[0]}/{segments[1]}/*";
    }
}

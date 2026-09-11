namespace Thor.Authorizer.Core.Policy;

public sealed class IamPolicyBuilder : IPolicyBuilder
{
    public PolicyDocument Build(AuthResult authResult, string methodArn)
    {
        if (!authResult.IsAllowed)
        {
            // Deny: minimal context, no principal-identifying info leaked into a denied response.
            return new PolicyDocument(
                PrincipalId: "unauthorized",
                Effect: "Deny",
                Resource: methodArn,
                Context: new Dictionary<string, string>());
        }

        var context = new Dictionary<string, string>
        {
            [AuthorizerContextKeys.TenantId] = authResult.TenantId,
            [AuthorizerContextKeys.CallerType] = authResult.CallerType,
            [AuthorizerContextKeys.PrincipalId] = authResult.PrincipalId,
            [AuthorizerContextKeys.Scopes] = string.Join(',', authResult.Scopes),
        };

        return new PolicyDocument(
            PrincipalId: authResult.PrincipalId,
            Effect: "Allow",
            Resource: methodArn,
            Context: context);
    }
}

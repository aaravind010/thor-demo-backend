using FluentAssertions;
using Thor.Authorizer.Core;
using Thor.Authorizer.Core.Policy;

namespace Thor.Authorizer.Test.Policy;

public class IamPolicyBuilderTests
{
    private readonly IamPolicyBuilder _builder = new();
    private const string MethodArn = "arn:aws:execute-api:us-east-1:123456789012:abc123/prod/GET/orders";

    [Fact]
    public void Build_AllowResult_ProducesExpectedContextAndEffect()
    {
        var authResult = AuthResult.Success("tenant-1", "principal-1", "user", ["read:orders", "write:orders"]);

        var policy = _builder.Build(authResult, MethodArn);

        policy.Effect.Should().Be("Allow");
        policy.PrincipalId.Should().Be("principal-1");
        policy.Resource.Should().Be(MethodArn);
        policy.Context.Should().HaveCount(4);
        policy.Context[AuthorizerContextKeys.TenantId].Should().Be("tenant-1");
        policy.Context[AuthorizerContextKeys.CallerType].Should().Be("user");
        policy.Context[AuthorizerContextKeys.PrincipalId].Should().Be("principal-1");
        policy.Context[AuthorizerContextKeys.Scopes].Should().Be("read:orders,write:orders");
    }

    [Fact]
    public void Build_AllowResultWithNoScopes_JoinsToEmptyString()
    {
        var authResult = AuthResult.Success("tenant-1", "principal-1", "machine", []);

        var policy = _builder.Build(authResult, MethodArn);

        policy.Context[AuthorizerContextKeys.Scopes].Should().Be(string.Empty);
    }

    [Fact]
    public void Build_DenyResult_ProducesEmptyContextAndSentinelPrincipal()
    {
        var authResult = AuthResult.Deny("tenant not resolved");

        var policy = _builder.Build(authResult, MethodArn);

        policy.Effect.Should().Be("Deny");
        policy.PrincipalId.Should().Be("unauthorized");
        policy.Resource.Should().Be(MethodArn);
        policy.Context.Should().BeEmpty();
    }
}

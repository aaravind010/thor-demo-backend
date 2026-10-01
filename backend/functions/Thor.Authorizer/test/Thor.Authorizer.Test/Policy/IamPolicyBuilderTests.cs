using FluentAssertions;
using Thor.Authorizer.Core;
using Thor.Authorizer.Core.Policy;

namespace Thor.Authorizer.Test.Policy;

public class IamPolicyBuilderTests
{
    private readonly IamPolicyBuilder _builder = new();
    private const string MethodArn = "arn:aws:execute-api:us-east-1:123456789012:abc123/prod/GET/orders";
    private const string ApiWideResource = "arn:aws:execute-api:us-east-1:123456789012:abc123/prod/*";

    [Fact]
    public void Build_AllowResult_ProducesExpectedContextAndEffect()
    {
        var authResult = AuthResult.Success("tenant-1", "principal-1", "user", ["read:orders", "write:orders"], ["admins", "auditors"]);

        var policy = _builder.Build(authResult, MethodArn);

        policy.Effect.Should().Be("Allow");
        policy.PrincipalId.Should().Be("principal-1");
        policy.Resource.Should().Be(ApiWideResource);
        policy.Context.Should().HaveCount(5);
        policy.Context[AuthorizerContextKeys.TenantId].Should().Be("tenant-1");
        policy.Context[AuthorizerContextKeys.CallerType].Should().Be("user");
        policy.Context[AuthorizerContextKeys.PrincipalId].Should().Be("principal-1");
        policy.Context[AuthorizerContextKeys.Scopes].Should().Be("read:orders,write:orders");
        policy.Context[AuthorizerContextKeys.Groups].Should().Be("admins,auditors");
    }

    [Fact]
    public void Build_AllowResultWithNoScopesOrGroups_JoinsToEmptyStrings()
    {
        var authResult = AuthResult.Success("tenant-1", "principal-1", "machine", []);

        var policy = _builder.Build(authResult, MethodArn);

        policy.Context[AuthorizerContextKeys.Scopes].Should().Be(string.Empty);
        policy.Context[AuthorizerContextKeys.Groups].Should().Be(string.Empty);
    }

    [Fact]
    public void Build_DenyResult_ProducesEmptyContextAndSentinelPrincipal()
    {
        var authResult = AuthResult.Deny("tenant not resolved");

        var policy = _builder.Build(authResult, MethodArn);

        policy.Effect.Should().Be("Deny");
        policy.PrincipalId.Should().Be("unauthorized");
        policy.Resource.Should().Be(ApiWideResource);
        policy.Context.Should().BeEmpty();
    }

    [Fact]
    public void Build_MethodArnWithoutStageOrMethod_ReturnsUnchanged()
    {
        var authResult = AuthResult.Deny("tenant not resolved");

        var policy = _builder.Build(authResult, "arn:aws:execute-api:us-east-1:123456789012:abc123");

        policy.Resource.Should().Be("arn:aws:execute-api:us-east-1:123456789012:abc123");
    }
}

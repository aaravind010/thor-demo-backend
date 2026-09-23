using Amazon.Lambda.APIGatewayEvents;
using Amazon.Lambda.Core;
using Microsoft.Extensions.DependencyInjection;
using Thor.Authorizer.Core;
using Thor.Authorizer.Core.Policy;

[assembly: LambdaSerializer(typeof(Amazon.Lambda.Serialization.SystemTextJson.DefaultLambdaJsonSerializer))]
[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("Thor.Authorizer.Test")]

namespace Thor.Authorizer.Function;

/// <summary>
/// Thin Lambda entry point — all logic lives in Thor.Authorizer.Core. This class only extracts
/// the headers API Gateway forwards and maps Core's AWS-agnostic PolicyDocument onto the real
/// APIGatewayCustomAuthorizerResponse.
/// </summary>
public sealed class Function
{
    private readonly AuthorizerHandler _handler;

    public Function() : this(CompositionRoot.BuildServiceProvider())
    {
    }

    internal Function(IServiceProvider serviceProvider)
    {
        _handler = serviceProvider.GetRequiredService<AuthorizerHandler>();
    }

    public async Task<APIGatewayCustomAuthorizerResponse> FunctionHandler(
        APIGatewayCustomAuthorizerRequest request, ILambdaContext context)
    {
        var hostHeader = GetHeader(request.Headers, "Host");
        var authorizationHeader = GetHeader(request.Headers, "Authorization");

        var policy = await _handler.HandleAsync(hostHeader, authorizationHeader, request.MethodArn, request.HttpMethod, request.Path);

        return MapToApiGatewayResponse(policy);
    }

    private static string? GetHeader(IDictionary<string, string>? headers, string name)
    {
        if (headers is null)
        {
            return null;
        }

        foreach (var (key, value) in headers)
        {
            if (string.Equals(key, name, StringComparison.OrdinalIgnoreCase))
            {
                return value;
            }
        }

        return null;
    }

    private static APIGatewayCustomAuthorizerResponse MapToApiGatewayResponse(PolicyDocument policy)
    {
        var context = new APIGatewayCustomAuthorizerContextOutput();
        foreach (var (key, value) in policy.Context)
        {
            context[key] = value;
        }

        return new APIGatewayCustomAuthorizerResponse
        {
            PrincipalID = policy.PrincipalId,
            PolicyDocument = new APIGatewayCustomAuthorizerPolicy
            {
                Version = "2012-10-17",
                Statement =
                [
                    new APIGatewayCustomAuthorizerPolicy.IAMPolicyStatement
                    {
                        Effect = policy.Effect,
                        Action = ["execute-api:Invoke"],
                        Resource = [policy.Resource],
                    }
                ],
            },
            Context = context,
        };
    }
}

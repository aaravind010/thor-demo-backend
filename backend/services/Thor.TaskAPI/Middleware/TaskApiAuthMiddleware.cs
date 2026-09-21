using Thor.Auth;

namespace Thor.TaskApi.Middleware;

/// <summary>
/// Requires a valid TaskApi-audience bearer JWT (minted by Thor.Api's connector registration
/// flow) on every request before it reaches a controller — see TaskAPI CLAUDE.md: "all
/// endpoints in the TaskAPI are protected by that JWT." Missing/invalid/expired/wrong-audience
/// tokens are rejected with 401 (fail closed).
/// </summary>
public sealed class TaskApiAuthMiddleware(RequestDelegate next, ITokenValidator tokenValidator)
{
    private const string BearerPrefix = "Bearer ";

    public async Task InvokeAsync(HttpContext context)
    {
        var authHeader = context.Request.Headers.Authorization.ToString();

        if (!authHeader.StartsWith(BearerPrefix, StringComparison.Ordinal))
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return;
        }

        var token = authHeader[BearerPrefix.Length..];
        var result = tokenValidator.Validate(token);

        if (!result.IsValid)
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return;
        }

        await next(context);
    }
}

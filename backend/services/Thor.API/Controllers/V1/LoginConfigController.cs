using Asp.Versioning;
using Microsoft.AspNetCore.Mvc;
using Thor.Api.Models;
using Thor.Api.Services;

namespace Thor.Api.Controllers.V1;

// Unauthenticated by design — the SPA calls it before sign-in (exempt from CognitoAuthMiddleware
// in Program.cs; its own authorization = NONE route in API Gateway's integration.tf).
[ApiController]
[ApiVersion("1.0")]
[Route("v{version:apiVersion}/login-config")]
public class LoginConfigController(LoginConfigService loginConfigService) : ControllerBase
{
    [HttpGet("{subdomain}")]
    public async Task<ActionResult<LoginConfigResponse>> Get(string subdomain, CancellationToken cancellationToken)
    {
        var config = await loginConfigService.GetAsync(subdomain, cancellationToken);

        // Same bare 404 for unknown and not-yet-active tenants, so the response reveals nothing
        // beyond whether a tenant can sign in.
        return config is null ? NotFound() : Ok(config);
    }
}

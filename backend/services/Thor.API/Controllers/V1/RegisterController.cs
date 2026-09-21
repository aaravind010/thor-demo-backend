using Asp.Versioning;
using Microsoft.AspNetCore.Mvc;
using Thor.Api.Constants;
using Thor.Api.Exceptions;
using Thor.Api.Models;
using Thor.Api.Services;

namespace Thor.Api.Controllers.V1;

[ApiController]
[ApiVersion("1.0")]
[Route("v{version:apiVersion}/register")]
public class RegisterController(IAuthService authService) : ControllerBase
{
    // Exchanges a connector's long-lived API key (x-task-api-key) for a short-lived,
    // role_type-scoped JWT plus a refresh token (ADR §5.2).
    [HttpPost]
    public async Task<ActionResult<TaskAuthResponse>> Register(
        [FromHeader(Name = AuthConstants.ApiKeyHeaderName)] string? apiKey,
        [FromBody] RegisterRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return BadRequest($"Missing '{AuthConstants.ApiKeyHeaderName}' header.");
        }

        if (string.IsNullOrWhiteSpace(request.RoleType))
        {
            return BadRequest("RoleType is required.");
        }

        try
        {
            return Ok(await authService.RegisterAsync(apiKey, request.RoleType, cancellationToken));
        }
        catch (InvalidApiKeyException)
        {
            return Unauthorized();
        }
        catch (ScopeNotGrantedException ex)
        {
            return StatusCode(StatusCodes.Status403Forbidden, ex.Message);
        }
    }

    [HttpPost("refresh")]
    public async Task<ActionResult<TaskAuthResponse>> Refresh(
        [FromBody] RefreshRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.RefreshToken))
        {
            return BadRequest("RefreshToken is required.");
        }

        try
        {
            return Ok(await authService.RefreshAsync(request.RefreshToken, cancellationToken));
        }
        catch (InvalidRefreshTokenException)
        {
            return Unauthorized();
        }
        catch (InvalidApiKeyException)
        {
            return Unauthorized();
        }
        catch (ScopeNotGrantedException ex)
        {
            return StatusCode(StatusCodes.Status403Forbidden, ex.Message);
        }
    }
}

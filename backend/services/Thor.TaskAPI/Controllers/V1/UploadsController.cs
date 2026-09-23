using Asp.Versioning;
using Microsoft.AspNetCore.Mvc;
using Thor.Core.Logging;
using Thor.DataConnectionManager.Exceptions;
using Thor.TaskApi.Constants;
using Thor.TaskApi.Models;
using Thor.TaskApi.Services;

namespace Thor.TaskApi.Controllers.V1;

[ApiController]
[ApiVersion("1.0")]
[Route("v{version:apiVersion}/uploads")]
public class UploadsController(UploadService uploadService, ILogger<UploadsController> logger) : ControllerBase
{
    // The Lambda authorizer validates the token/API key and sets
    // X-THOR-TENANT-ID; requests reaching Thor.TaskAPI are trusted, so the header is read
    // directly rather than re-verified here.
    [HttpPost("presigned-url")]
    public async Task<ActionResult<PresignedUploadResponse>> CreatePresignedUrl(
        [FromBody] PresignedUploadRequest request, CancellationToken cancellationToken)
    {
        if (!Request.Headers.TryGetValue(UploadConstants.TenantHeaderName, out var tenantHeaderValue) ||
            !Guid.TryParse(tenantHeaderValue, out var tenantId))
        {
            return BadRequest($"Missing or invalid '{UploadConstants.TenantHeaderName}' header.");
        }

        if (string.IsNullOrWhiteSpace(request.ContentType))
        {
            return BadRequest("ContentType is required.");
        }

        using var _ = ThorLogContext.PushProperties(new Dictionary<string, object?>
        {
            ["TaskId"] = request.TaskId,
            ["SourceId"] = request.SourceId,
        });

        try
        {
            var response = await uploadService.CreatePresignedUploadUrlAsync(tenantId, request, cancellationToken);
            return Ok(response);
        }
        catch (TenantNotFoundException ex)
        {
            logger.LogWarning(ex, "Presigned URL request rejected: tenant {TenantId} not found", tenantId);
            return StatusCode(StatusCodes.Status403Forbidden);
        }
    }
}

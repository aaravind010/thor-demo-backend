using Amazon.S3;
using Amazon.S3.Model;
using Thor.DataConnectionManager.Routing;
using Thor.TaskApi.Constants;
using Thor.TaskApi.Models;

namespace Thor.TaskApi.Services;

/// <summary>
/// Generates tenant-scoped presigned S3 upload URLs. Per ADR §5.3, the presigned URL is the
/// primary isolation boundary for uploads: the tenant segment of the object key is hard-bound
/// to a tenant re-verified against the Master metadata store, never to caller-supplied input.
/// </summary>
public sealed class UploadService(IAmazonS3 s3Client, ITenantRoutingResolver tenantRoutingResolver, UploadOptions options)
{
    public async Task<PresignedUploadResponse> CreatePresignedUploadUrlAsync(
        Guid tenantId, PresignedUploadRequest request, CancellationToken cancellationToken = default)
    {
        // tenantId comes from the trusted X-THOR-TENANT-ID header (set by the Lambda
        // authorizer), but routing is still confirmed here — a valid tenant ID doesn't
        // guarantee the tenant is fully provisioned. Fail closed per ADR §5.4.
        await tenantRoutingResolver.ResolveAsync(tenantId, cancellationToken);
        
        var fileName = $"data_{request.TaskId}_{request.SourceId}_{DateTimeOffset.UtcNow:yyyyMMddHHmmss}";

        var objectKey = $"tenants/{tenantId}/uploads/{request.SourceId}/{fileName}";
        var expiresAtUtc = DateTime.UtcNow.Add(UploadConstants.PresignedUrlLifetime);

        var presignedRequest = new GetPreSignedUrlRequest
        {
            BucketName = options.BucketName,
            Key = objectKey,
            Verb = HttpVerb.PUT,
            Expires = expiresAtUtc,
            ContentType = request.ContentType,
        };

        var uploadUrl = await s3Client.GetPreSignedURLAsync(presignedRequest);

        return new PresignedUploadResponse(uploadUrl, objectKey, expiresAtUtc);
    }
}

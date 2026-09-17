namespace Thor.S3;

/// <summary>
/// Parses an upload object key (<c>tenants/{tenantId}/uploads/{scanId}/{sourceId}/{fileName}</c>)
/// back into its identifiers. Shared between extraction (per-file <c>SourceId</c>/<c>ScanId</c>
/// resolution) and Create manifest Lambda
/// </summary>
public static class UploadKeyParser
{
    public static (Guid TenantId, Guid SourceId, Guid ScanId)? TryParse(string key)
    {
        var segments = key.Split('/');
        if (segments.Length < 6 || segments[0] != "tenants" || segments[2] != "uploads")
        {
            return null;
        }

        if (!Guid.TryParse(segments[1], out var tenantId)
            || !Guid.TryParse(segments[3], out var scanId)
            || !Guid.TryParse(segments[4], out var sourceId))
        {
            return null;
        }

        return (tenantId, sourceId, scanId);
    }
}

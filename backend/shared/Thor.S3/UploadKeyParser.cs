namespace Thor.S3;

/// <summary>
/// Parses an upload object key (<c>tenants/{tenantId}/uploads/{scanId}/{sourceId}/{fileName}</c>)
/// back into its identifiers. Shared between extraction (per-file <c>SourceId</c>/<c>ScanId</c>
/// resolution) and Create manifest Lambda
/// </summary>
public static class UploadKeyParser
{
    /// <summary>
    /// <c>TaskId</c> is <c>null</c> unless the file name carries one — see
    /// <see cref="TryParseTaskId"/>. Every other field is required, and a key missing any of them
    /// parses to <c>null</c> as a whole.
    /// </summary>
    public static (Guid TenantId, Guid SourceId, Guid ScanId, Guid? TaskId)? TryParse(string key)
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

        return (tenantId, sourceId, scanId, TryParseTaskId(segments[5]));
    }

    /// <summary>
    /// Recovers the ScanTask id from the file name Thor.TaskApi mints,
    /// <c>data_{taskId}_{sourceId}_{uuidv7}</c> (Services/UploadService.cs).
    ///
    /// <para>This is the trustworthy half of the key: UploadService took that <c>taskId</c> from a
    /// ScanTask it had already looked up in the tenant DB, whereas the <c>{sourceId}</c> path
    /// segment is whatever the caller asked to be presigned and is never checked against that task.
    /// A name in any other shape returns <c>null</c> rather than failing the parse — uploads that
    /// did not come through the presign path still ingest, they just cannot be attributed to a
    /// task.</para>
    /// </summary>
    private static Guid? TryParseTaskId(string fileName)
    {
        var parts = fileName.Split('_');
        return parts.Length >= 4 && parts[0] == "data" && Guid.TryParse(parts[1], out var taskId)
            ? taskId
            : null;
    }
}

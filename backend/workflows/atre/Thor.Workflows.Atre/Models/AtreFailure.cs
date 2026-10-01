namespace Thor.Workflows.Atre.Models;

/// <summary>
/// What Step Functions writes to <c>$.Error</c> when a <c>Catch</c> fires. Both fields are optional
/// because the shape depends on how the state failed: a Lambda that threw gives an exception type and
/// a serialized stack, while a service-level failure like <c>States.Timeout</c> gives only the error
/// name.
/// </summary>
public sealed record AtreFailureCause(string? Error, string? Cause);

/// <summary>
/// What <see cref="Steps.AtreRecordFailureStep"/> is invoked with. The run's identity comes from
/// <c>$.Request</c>, which every Catch in the definition preserves, and the cause from the
/// <c>ResultPath</c> that Catch wrote.
/// </summary>
public sealed record AtreFailureRequest(AtreRequest Request, AtreFailureCause? Error);

/// <summary>
/// What <see cref="Steps.AtreRecordChunkFailureStep"/> is invoked with, on a Map item's Catch path.
/// It is the item itself — an <see cref="AtreChunkRequest"/>'s fields, so the window being lost is
/// known — plus the error the Catch merged into it. Flat rather than nested because the Catch writes
/// into the item, so <c>$</c> at that point already carries both.
/// </summary>
public sealed record AtreChunkFailureRequest(
    Guid TenantId,
    Guid? ScanManifestId,
    Guid? RunId,
    int Offset,
    int Limit,
    AtreFailureCause? Error);

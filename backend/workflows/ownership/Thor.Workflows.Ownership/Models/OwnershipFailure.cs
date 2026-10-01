namespace Thor.Workflows.Ownership.Models;

/// <summary>
/// What Step Functions writes to <c>$.Error</c> when a <c>Catch</c> fires. Both fields are optional
/// because the shape depends on how the state failed: a Lambda that threw gives an exception type and
/// a serialized stack, while a service-level failure like <c>States.Timeout</c> gives only the error
/// name.
/// </summary>
public sealed record OwnershipFailureCause(string? Error, string? Cause);

/// <summary>
/// What <see cref="Steps.OwnershipRecordFailureStep"/> is invoked with: the run's identity from
/// <c>$.Request</c>, which every Catch in the definition preserves, and the cause the Catch wrote.
/// </summary>
public sealed record OwnershipFailureRequest(OwnershipRequest Request, OwnershipFailureCause? Error);

/// <summary>
/// What <see cref="Steps.OwnershipRecordChunkFailureStep"/> is invoked with, on a Map item's Catch
/// path: the item's own fields, so the window being lost is known, plus the error the Catch merged
/// into it. Flat because the Catch writes into the item.
/// </summary>
public sealed record OwnershipChunkFailureRequest(
    Guid TenantId,
    Guid? ScanManifestId,
    Guid? RunId,
    string? EntityType,
    int Offset,
    int Limit,
    OwnershipFailureCause? Error);

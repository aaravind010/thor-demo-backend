using Thor.DataLayer.Models.Tenants;

namespace Thor.TaskApi.Models;

/// <summary>
/// Request body for <c>PUT /task/{taskId}/status</c>. <see cref="Status"/> is one of
/// <see cref="ScanTaskStatus"/>'s settable values (e.g. "completed"), not a
/// <see cref="Thor.DataLayer.Models.Tenants.ScanTaskStatus"/> value.
/// </summary>
public sealed record UpdateTaskStatusRequest(string Status);

/// <summary>
/// Wire-level status values a connector may report via <c>PUT /tasks/{taskId}/status</c>.
/// Kept separate from <see cref="ScanTaskStatus"/> (the DB-layer vocabulary) so the request
/// payload never has to mirror the DB's casing/values; each settable value maps 1:1 onto the
/// <see cref="ScanTaskStatus"/> a connector is allowed to set.
/// </summary>
public static class ScanTaskStatus
{
    public const string Pending = "pending";
    public const string Completed = "completed";
    public const string Failed = "failed";

    private static readonly IReadOnlyDictionary<string, string> ScanTaskStatusByValue = new Dictionary<string, string>
    {
        [Pending] = ScanTaskStatus.Pending,
        [Completed] = ScanTaskStatus.Completed,
        [Failed] = ScanTaskStatus.Failed,
    };

    /// <summary>The values accepted in the request payload, for use in validation error messages.</summary>
    public static IReadOnlyCollection<string> SettableValues => (IReadOnlyCollection<string>)ScanTaskStatusByValue.Keys;

    /// <summary>Maps a wire-level status value to its <see cref="ScanTaskStatus"/> equivalent.</summary>
    public static bool Validate(string? value, out string scanTaskStatus)
    {
        if (value is not null && ScanTaskStatusByValue.TryGetValue(value, out var mapped))
        {
            scanTaskStatus = mapped;
            return true;
        }

        scanTaskStatus = string.Empty;
        return false;
    }
}

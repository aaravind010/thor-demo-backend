namespace Thor.Api.Exceptions;

/// <summary>The submitted values don't exactly match the fields required by the authentication type.</summary>
public sealed class InvalidAuthenticationFieldsException(IReadOnlyList<Guid> missingFieldIds, IReadOnlyList<Guid> unknownFieldIds)
    : Exception(BuildMessage(missingFieldIds, unknownFieldIds))
{
    public IReadOnlyList<Guid> MissingFieldIds { get; } = missingFieldIds;

    public IReadOnlyList<Guid> UnknownFieldIds { get; } = unknownFieldIds;

    private static string BuildMessage(IReadOnlyList<Guid> missingFieldIds, IReadOnlyList<Guid> unknownFieldIds)
    {
        var parts = new List<string>();

        if (missingFieldIds.Count > 0)
        {
            parts.Add($"missing field(s): {string.Join(", ", missingFieldIds)}");
        }

        if (unknownFieldIds.Count > 0)
        {
            parts.Add($"unrecognized field(s): {string.Join(", ", unknownFieldIds)}");
        }

        return $"Invalid authentication values — {string.Join("; ", parts)}.";
    }
}

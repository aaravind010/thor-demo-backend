namespace Thor.Api.Exceptions;

/// <summary>The submitted config values don't match the connector's required config fields.</summary>
public sealed class InvalidScanConnectorConfigValuesException(IReadOnlyList<Guid> missingConfigIds, IReadOnlyList<Guid> unknownConfigIds)
    : Exception(BuildMessage(missingConfigIds, unknownConfigIds))
{
    public IReadOnlyList<Guid> MissingConfigIds { get; } = missingConfigIds;

    public IReadOnlyList<Guid> UnknownConfigIds { get; } = unknownConfigIds;

    private static string BuildMessage(IReadOnlyList<Guid> missingConfigIds, IReadOnlyList<Guid> unknownConfigIds)
    {
        var parts = new List<string>();

        if (missingConfigIds.Count > 0)
        {
            parts.Add($"missing required config field(s): {string.Join(", ", missingConfigIds)}");
        }

        if (unknownConfigIds.Count > 0)
        {
            parts.Add($"unrecognized config field(s): {string.Join(", ", unknownConfigIds)}");
        }

        return $"Invalid connector config values — {string.Join("; ", parts)}.";
    }
}

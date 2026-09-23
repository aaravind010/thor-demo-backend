using System.Text.Json.Serialization;

namespace Thor.Graph.BulkLoad;

/// <summary>Coarse status bucket every documented Neptune loader status maps to — the specific raw AWS status string is kept on <see cref="BulkLoadStatusResult.RawStatus"/> for logging.</summary>
public enum BulkLoadStatus
{
    InProgress,
    Completed,
    Failed,
}

public sealed record BulkLoadStartResult(string LoadId);

public sealed record BulkLoadStatusResult(
    string LoadId,
    BulkLoadStatus Status,
    string RawStatus,
    long? TotalRecords,
    long? ParsingErrors,
    long? DatatypeMismatchErrors,
    long? InsertErrors,
    IReadOnlyList<string> ErrorMessages)
{
    /// <summary>True if the load reported LOAD_COMPLETED but individual rows still failed — a completed job is not the same as an all-succeeded job.</summary>
    public bool HasRowErrors => (ParsingErrors ?? 0) > 0 || (DatatypeMismatchErrors ?? 0) > 0 || (InsertErrors ?? 0) > 0;
}

internal sealed record StartLoadRequestBody(
    [property: JsonPropertyName("source")] string Source,
    [property: JsonPropertyName("format")] string Format,
    [property: JsonPropertyName("iamRoleArn")] string IamRoleArn,
    [property: JsonPropertyName("region")] string Region,
    [property: JsonPropertyName("failOnError")] string FailOnError,
    [property: JsonPropertyName("parallelism")] string Parallelism);

internal sealed record StartLoadResponse([property: JsonPropertyName("payload")] StartLoadPayload? Payload);
internal sealed record StartLoadPayload([property: JsonPropertyName("loadId")] string LoadId);

internal sealed record LoadStatusResponse([property: JsonPropertyName("payload")] LoadStatusPayload? Payload);

internal sealed record LoadStatusPayload(
    [property: JsonPropertyName("overallStatus")] LoadOverallStatus? OverallStatus,
    [property: JsonPropertyName("errors")] LoadErrors? Errors);

internal sealed record LoadOverallStatus(
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("totalRecords")] long? TotalRecords,
    [property: JsonPropertyName("parsingErrors")] long? ParsingErrors,
    [property: JsonPropertyName("datatypeMismatchErrors")] long? DatatypeMismatchErrors,
    [property: JsonPropertyName("insertErrors")] long? InsertErrors);

internal sealed record LoadErrors([property: JsonPropertyName("errorLogs")] IReadOnlyList<LoadErrorLog>? ErrorLogs);

internal sealed record LoadErrorLog([property: JsonPropertyName("errorMessage")] string? ErrorMessage);

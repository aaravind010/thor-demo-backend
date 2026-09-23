namespace Thor.Workflows.IngestionDriver.Core;

/// <summary>The driver's decision, serialized straight back as the Lambda response for a Step Functions Choice state to read.</summary>
public sealed record ComputeSelectionResult(string ComputeTarget, long TotalSizeBytes, int FileCount);

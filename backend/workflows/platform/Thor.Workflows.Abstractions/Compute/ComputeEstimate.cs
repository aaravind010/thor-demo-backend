namespace Thor.Workflows.Abstractions.Compute;

/// <summary>
/// How much work a run represents, as measured by the workflow itself. This is the only part of
/// compute selection a workflow has to implement — the threshold and the resulting decision are
/// shared, because "how big is this?" is the only question whose answer differs per workflow.
/// </summary>
/// <param name="TotalBytes">Total size of the run's input, however the workflow counts that.</param>
/// <param name="ItemCount">How many discrete items the run covers. Carried for observability; the default policy does not branch on it.</param>
public sealed record ComputeEstimate(long TotalBytes, int ItemCount);

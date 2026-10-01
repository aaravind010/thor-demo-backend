namespace Thor.Workflows.Abstractions.Compute;

/// <summary>
/// The compute decision for a run, and the evidence behind it.
///
/// <para>This is serialized as the select-compute step's Lambda response, so the field names are part
/// of the state machine's contract: its <c>Choice</c> branches on <c>Target</c>. The other two fields
/// are not read by the state machine — they are there so an execution history shows why a run went
/// the way it did.</para>
/// </summary>
/// <param name="Target">One of <see cref="ComputeTarget.Lambda"/> or <see cref="ComputeTarget.EcsTask"/>.</param>
public sealed record ComputePlan(string Target, long TotalBytes, int ItemCount);

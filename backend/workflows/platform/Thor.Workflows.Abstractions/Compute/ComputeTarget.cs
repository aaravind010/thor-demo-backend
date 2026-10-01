namespace Thor.Workflows.Abstractions.Compute;

/// <summary>
/// The compute targets a workflow step can run on. Both are the same container image; which one a
/// given invocation uses is the state machine's choice, and these are the values it branches on.
/// </summary>
public static class ComputeTarget
{
    public const string Lambda = "lambda";

    public const string EcsTask = "ecs-task";
}

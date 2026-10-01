namespace Thor.Workflows.Abstractions;

/// <summary>
/// Environment variables every workflow module's container reads, whichever compute target it runs on.
/// Terraform sets these on both the ECS task definitions and the per-step Lambda functions.
/// </summary>
public static class WorkflowEnvironment
{
    /// <summary>Which <see cref="IWorkflowStep"/> this invocation runs. Set on both compute targets.</summary>
    public const string Step = "THOR_STEP";

    /// <summary>
    /// The step's input, as JSON. Set only on ECS, where the state machine stringifies it into a
    /// container override — on Lambda the input arrives as the invocation event instead.
    /// </summary>
    public const string Input = "THOR_INPUT";

    /// <summary>Set by the Lambda execution environment, never under ECS. This is how the image knows which host to start.</summary>
    public const string LambdaRuntimeApi = "AWS_LAMBDA_RUNTIME_API";
}

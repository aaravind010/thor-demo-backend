namespace Thor.Workflows.Abstractions;

/// <summary>
/// What a step hands back.
///
/// <para><paramref name="Value"/> is the step's own result object. On Lambda it is serialized as the
/// invocation response, so a Step Functions <c>Choice</c> can branch on whatever fields it carries.
/// On ECS it is unobservable: <c>ecs:runTask.sync</c> returns the ECS task description, not the
/// container's output, and there is no other channel. A step whose result something downstream reads
/// must therefore be declared always-lambda, or write that result somewhere the state machine can
/// read it.</para>
///
/// <para><paramref name="IsInProgress"/> is for steps that poll an external job. It is advisory
/// metadata for the host; a step that needs the orchestrator to branch on it must also expose it on
/// <paramref name="Value"/>, which is what <c>GraphLoadPollResult.IsInProgress</c> does. It is
/// deliberately not mapped to a distinct process exit code — see <c>EcsHost</c>.</para>
/// </summary>
public sealed record StepResult(object? Value, bool IsInProgress = false)
{
    /// <summary>
    /// A step that produced nothing a caller needs.
    ///
    /// <para>The argument is named deliberately. A bare <c>new(null)</c> is ambiguous between the
    /// primary constructor and the record's compiler-generated copy constructor — and the copy
    /// constructor wins, because <c>StepResult</c> is more derived than <c>object?</c>. That compiles
    /// and then throws at static initialization.</para>
    /// </summary>
    public static StepResult None { get; } = new(Value: null);

    public static StepResult Completed(object? value) => new(value);

    public static StepResult InProgress(object? value) => new(value, IsInProgress: true);
}

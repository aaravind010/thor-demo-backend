using Microsoft.Extensions.Logging.Abstractions;
using Thor.Workflows.Abstractions;
using Xunit;

namespace Thor.Workflows.Hosting.Tests;

public sealed class EcsHostTests
{
    private sealed class RecordingStep : IWorkflowStep
    {
        public string? ReceivedInput { get; private set; }

        public Task<StepResult> ExecuteAsync(string inputJson, CancellationToken cancellationToken)
        {
            ReceivedInput = inputJson;
            return Task.FromResult(StepResult.None);
        }
    }

    private sealed class ThrowingStep : IWorkflowStep
    {
        public Task<StepResult> ExecuteAsync(string inputJson, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("simulated step failure");
    }

    private sealed class InProgressStep : IWorkflowStep
    {
        public Task<StepResult> ExecuteAsync(string inputJson, CancellationToken cancellationToken) =>
            Task.FromResult(StepResult.InProgress(new { Outcome = "in-progress" }));
    }

    [Fact]
    public async Task RunAsync_KnownStepWithInput_RunsStepAndReturnsZero()
    {
        var step = new RecordingStep();
        var steps = new Dictionary<string, Func<IWorkflowStep>> { ["extract-stage"] = () => step };

        var exitCode = await EcsHost.RunAsync(steps, NullLogger.Instance, "extract-stage", """{"tenantId":"t1"}""");

        Assert.Equal(0, exitCode);
        Assert.Equal("""{"tenantId":"t1"}""", step.ReceivedInput);
    }

    [Fact]
    public async Task RunAsync_MissingStepName_ReturnsNonZero_WithoutInvokingAnyStep()
    {
        var steps = new Dictionary<string, Func<IWorkflowStep>> { ["extract-stage"] = () => new RecordingStep() };

        var exitCode = await EcsHost.RunAsync(steps, NullLogger.Instance, stepName: null, inputJson: """{"tenantId":"t1"}""");

        Assert.Equal(1, exitCode);
    }

    [Fact]
    public async Task RunAsync_UnknownStepName_ReturnsNonZero()
    {
        var steps = new Dictionary<string, Func<IWorkflowStep>> { ["extract-stage"] = () => new RecordingStep() };

        var exitCode = await EcsHost.RunAsync(steps, NullLogger.Instance, "no-such-step", """{"tenantId":"t1"}""");

        Assert.Equal(1, exitCode);
    }

    [Fact]
    public async Task RunAsync_MissingInput_ReturnsNonZero_WithoutInvokingStep()
    {
        var step = new RecordingStep();
        var steps = new Dictionary<string, Func<IWorkflowStep>> { ["extract-stage"] = () => step };

        var exitCode = await EcsHost.RunAsync(steps, NullLogger.Instance, "extract-stage", inputJson: null);

        Assert.Equal(1, exitCode);
        Assert.Null(step.ReceivedInput);
    }

    [Fact]
    public async Task RunAsync_StepThrows_ReturnsNonZero_DoesNotPropagateException()
    {
        var steps = new Dictionary<string, Func<IWorkflowStep>> { ["graph-load-poll"] = () => new ThrowingStep() };

        var exitCode = await EcsHost.RunAsync(steps, NullLogger.Instance, "graph-load-poll", """{"scanId":"s1"}""");

        Assert.Equal(1, exitCode);
    }

    /// <summary>
    /// The ECS target has no way to report "still in progress" — under ecs:runTask.sync any non-zero
    /// exit from the essential container is States.TaskFailed, so a distinct retry-later code would be
    /// read as a failure and sent to the DLQ while the job it was polling was still running. Exit 0 is
    /// the only honest answer here; polling steps run on Lambda, where the flag round-trips on the
    /// result object.
    /// </summary>
    [Fact]
    public async Task RunAsync_StepReportsInProgress_StillReturnsZero()
    {
        var steps = new Dictionary<string, Func<IWorkflowStep>> { ["graph-load-poll"] = () => new InProgressStep() };

        var exitCode = await EcsHost.RunAsync(steps, NullLogger.Instance, "graph-load-poll", """{"scanId":"s1"}""");

        Assert.Equal(0, exitCode);
    }
}

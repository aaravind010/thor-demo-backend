using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Thor.Workflows.Abstractions.Tests;

public sealed class WorkflowHostTests
{
    private sealed class RecordingStep : IWorkflowStep
    {
        public string? ReceivedInput { get; private set; }

        public Task ExecuteAsync(string inputJson, CancellationToken cancellationToken)
        {
            ReceivedInput = inputJson;
            return Task.CompletedTask;
        }
    }

    private sealed class ThrowingStep : IWorkflowStep
    {
        public Task ExecuteAsync(string inputJson, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("simulated step failure");
    }

    [Fact]
    public async Task RunAsync_KnownStepWithInput_RunsStepAndReturnsZero()
    {
        var step = new RecordingStep();
        var steps = new Dictionary<string, Func<IWorkflowStep>> { ["extract-stage"] = () => step };

        var exitCode = await WorkflowHost.RunAsync(steps, NullLogger.Instance, "extract-stage", """{"tenantId":"t1"}""");

        Assert.Equal(0, exitCode);
        Assert.Equal("""{"tenantId":"t1"}""", step.ReceivedInput);
    }

    [Fact]
    public async Task RunAsync_MissingStepName_ReturnsNonZero_WithoutInvokingAnyStep()
    {
        var steps = new Dictionary<string, Func<IWorkflowStep>> { ["extract-stage"] = () => new RecordingStep() };

        var exitCode = await WorkflowHost.RunAsync(steps, NullLogger.Instance, stepName: null, inputJson: """{"tenantId":"t1"}""");

        Assert.Equal(1, exitCode);
    }

    [Fact]
    public async Task RunAsync_UnknownStepName_ReturnsNonZero()
    {
        var steps = new Dictionary<string, Func<IWorkflowStep>> { ["extract-stage"] = () => new RecordingStep() };

        var exitCode = await WorkflowHost.RunAsync(steps, NullLogger.Instance, "no-such-step", """{"tenantId":"t1"}""");

        Assert.Equal(1, exitCode);
    }

    [Fact]
    public async Task RunAsync_MissingInput_ReturnsNonZero_WithoutInvokingStep()
    {
        var step = new RecordingStep();
        var steps = new Dictionary<string, Func<IWorkflowStep>> { ["extract-stage"] = () => step };

        var exitCode = await WorkflowHost.RunAsync(steps, NullLogger.Instance, "extract-stage", inputJson: null);

        Assert.Equal(1, exitCode);
        Assert.Null(step.ReceivedInput);
    }

    [Fact]
    public async Task RunAsync_StepThrows_ReturnsNonZero_DoesNotPropagateException()
    {
        var steps = new Dictionary<string, Func<IWorkflowStep>> { ["graph-load-poll"] = () => new ThrowingStep() };

        var exitCode = await WorkflowHost.RunAsync(steps, NullLogger.Instance, "graph-load-poll", """{"scanId":"s1"}""");

        Assert.Equal(1, exitCode);
    }
}

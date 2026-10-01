using System.Text;
using Amazon.Lambda.Core;
using Microsoft.Extensions.Logging.Abstractions;
using Thor.Workflows.Abstractions;
using Xunit;

namespace Thor.Workflows.Hosting.Tests;

/// <summary>Drives <see cref="LambdaHost.BuildHandler"/> directly — no real Lambda Runtime API loop is available in a test.</summary>
public sealed class LambdaHostTests
{
    private sealed class FakeLambdaContext : ILambdaContext
    {
        public string AwsRequestId => "test-request-id";
        public IClientContext ClientContext => throw new NotSupportedException();
        public string FunctionName => "test-function";
        public string FunctionVersion => "1";
        public ICognitoIdentity Identity => throw new NotSupportedException();
        public string InvokedFunctionArn => throw new NotSupportedException();
        public ILambdaLogger Logger => throw new NotSupportedException();
        public string LogGroupName => throw new NotSupportedException();
        public string LogStreamName => throw new NotSupportedException();
        public int MemoryLimitInMB => 512;
        public TimeSpan RemainingTime => TimeSpan.FromSeconds(30);
    }

    private sealed record SampleResult(string Outcome, bool IsInProgress);

    private sealed class RecordingStep(StepResult result) : IWorkflowStep
    {
        public string? ReceivedInput { get; private set; }

        public Task<StepResult> ExecuteAsync(string inputJson, CancellationToken cancellationToken)
        {
            ReceivedInput = inputJson;
            return Task.FromResult(result);
        }
    }

    private sealed class ThrowingStep : IWorkflowStep
    {
        public Task<StepResult> ExecuteAsync(string inputJson, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("simulated step failure");
    }

    private static Stream ToStream(string s) => new MemoryStream(Encoding.UTF8.GetBytes(s));

    private static async Task<string> ReadAllAsync(Stream stream)
    {
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return await reader.ReadToEndAsync();
    }

    [Fact]
    public async Task Handler_SerializesStepResultValueDirectly()
    {
        var step = new RecordingStep(StepResult.InProgress(new SampleResult("in-progress", true)));
        var handler = LambdaHost.BuildHandler(step, "graph-load-poll", NullLogger.Instance);

        var responseStream = await handler(ToStream("""{"scanId":"s1"}"""), new FakeLambdaContext());

        var responseJson = await ReadAllAsync(responseStream);
        Assert.Contains("\"Outcome\":\"in-progress\"", responseJson);
        Assert.Contains("\"IsInProgress\":true", responseJson);
        Assert.Equal("""{"scanId":"s1"}""", step.ReceivedInput);
    }

    /// <summary>
    /// The response is the result's Value, not the StepResult wrapper — a Step Functions Choice reads
    /// the step's own fields off the Lambda output, so wrapping it would break every existing path.
    /// </summary>
    [Fact]
    public async Task Handler_DoesNotWrapTheResultEnvelope()
    {
        var step = new RecordingStep(StepResult.Completed(new SampleResult("done", false)));
        var handler = LambdaHost.BuildHandler(step, "promote", NullLogger.Instance);

        var responseJson = await ReadAllAsync(await handler(ToStream("{}"), new FakeLambdaContext()));

        Assert.DoesNotContain("\"Value\"", responseJson);
        Assert.StartsWith("{\"Outcome\"", responseJson);
    }

    [Fact]
    public async Task Handler_StepThrows_PropagatesException()
    {
        var handler = LambdaHost.BuildHandler(new ThrowingStep(), "graph-load-poll", NullLogger.Instance);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => handler(ToStream("""{"scanId":"s1"}"""), new FakeLambdaContext()));
    }
}

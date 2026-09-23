using System.Text;
using Amazon.Lambda.Core;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Thor.Workflows.Abstractions.Tests;

/// <summary>Drives <see cref="LambdaEntry.BuildHandler"/> directly — no real Lambda Runtime API loop is available in a test.</summary>
public sealed class LambdaEntryTests
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

    private sealed class RecordingStep(object? result, bool isInProgress = false) : IWorkflowStep
    {
        public string? ReceivedInput { get; private set; }

        public Task<(object? Result, bool IsInProgress)> ExecuteAsync(string inputJson, CancellationToken cancellationToken)
        {
            ReceivedInput = inputJson;
            return Task.FromResult((result, isInProgress));
        }
    }

    private sealed class ThrowingStep : IWorkflowStep
    {
        public Task<(object? Result, bool IsInProgress)> ExecuteAsync(string inputJson, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("simulated step failure");
    }

    private static Stream ToStream(string s) => new MemoryStream(Encoding.UTF8.GetBytes(s));

    private static async Task<string> ReadAllAsync(Stream stream)
    {
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return await reader.ReadToEndAsync();
    }

    [Fact]
    public async Task Handler_SerializesStepResultDirectly()
    {
        var step = new RecordingStep(new SampleResult("in-progress", true));
        var handler = LambdaEntry.BuildHandler(step, "graph-load-poll", NullLogger.Instance);

        var responseStream = await handler(ToStream("""{"scanId":"s1"}"""), new FakeLambdaContext());

        var responseJson = await ReadAllAsync(responseStream);
        Assert.Contains("\"Outcome\":\"in-progress\"", responseJson);
        Assert.Contains("\"IsInProgress\":true", responseJson);
        Assert.Equal("""{"scanId":"s1"}""", step.ReceivedInput);
    }

    [Fact]
    public async Task Handler_StepThrows_PropagatesException()
    {
        var handler = LambdaEntry.BuildHandler(new ThrowingStep(), "graph-load-poll", NullLogger.Instance);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => handler(ToStream("""{"scanId":"s1"}"""), new FakeLambdaContext()));
    }
}

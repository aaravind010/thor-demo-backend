using System.Net;
using System.Net.Http.Json;
using System.Text;
using Thor.Graph.BulkLoad;
using Xunit;

namespace Thor.Workflows.Ingestion.Tests.Graph;

public sealed class NeptuneBulkLoaderClientTests
{
    private sealed class FakeHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public HttpRequestMessage? LastRequest { get; private set; }
        public string? LastRequestBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            if (request.Content is not null)
            {
                LastRequestBody = await request.Content.ReadAsStringAsync(cancellationToken);
            }
            return respond(request);
        }
    }

    /// <summary>Returns a fixed sequence of responses in order, one per call — used to test the retry behavior in <see cref="NeptuneBulkLoaderClient"/>.</summary>
    private sealed class SequencedHttpMessageHandler(params HttpResponseMessage[] responsesInOrder) : HttpMessageHandler
    {
        public int CallCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var index = Math.Min(CallCount, responsesInOrder.Length - 1);
            CallCount++;
            return Task.FromResult(responsesInOrder[index]);
        }
    }

    private static HttpResponseMessage JsonResponse(string json) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(json, Encoding.UTF8, "application/json"),
    };

    private static HttpResponseMessage StartLoadResponse(string loadId) =>
        new(HttpStatusCode.OK) { Content = JsonContent.Create(new { payload = new { loadId } }) };

    private static HttpResponseMessage StatusResponse(string status) =>
        new(HttpStatusCode.OK) { Content = JsonContent.Create(new { payload = new { overallStatus = new { status } } }) };

    private static NeptuneBulkLoaderClient BuildClient(HttpMessageHandler handler)
    {
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://neptune-test:8182/") };
        return new NeptuneBulkLoaderClient(httpClient);
    }

    [Fact]
    public async Task StartLoadAsync_PostsToLoaderEndpoint_ReturnsLoadId()
    {
        var handler = new FakeHttpMessageHandler(_ => JsonResponse("""{"status":"200 OK","payload":{"loadId":"abc-123"}}"""));
        using var client = BuildClient(handler);

        var result = await client.StartLoadAsync(new Uri("s3://bucket/prefix/"), "arn:aws:iam::123:role/neptune-load", "us-east-1");

        Assert.Equal("abc-123", result.LoadId);
        Assert.Equal(HttpMethod.Post, handler.LastRequest!.Method);
        Assert.Equal("https://neptune-test:8182/loader", handler.LastRequest.RequestUri!.ToString());
        Assert.Contains("\"iamRoleArn\":\"arn:aws:iam::123:role/neptune-load\"", handler.LastRequestBody);
        Assert.Contains("\"source\":\"s3://bucket/prefix/\"", handler.LastRequestBody);
    }

    [Fact]
    public async Task GetLoadStatusAsync_InProgress_MapsToInProgress()
    {
        var handler = new FakeHttpMessageHandler(_ => JsonResponse(
            """{"status":"200 OK","payload":{"overallStatus":{"status":"LOAD_IN_PROGRESS","totalRecords":0,"parsingErrors":0,"datatypeMismatchErrors":0,"insertErrors":0}}}"""));
        using var client = BuildClient(handler);

        var status = await client.GetLoadStatusAsync("abc-123");

        Assert.Equal(BulkLoadStatus.InProgress, status.Status);
        Assert.False(status.HasRowErrors);
        Assert.Contains("loader/abc-123", handler.LastRequest!.RequestUri!.ToString());
    }

    [Fact]
    public async Task GetLoadStatusAsync_Completed_MapsToCompletedWithNoRowErrors()
    {
        var handler = new FakeHttpMessageHandler(_ => JsonResponse(
            """{"status":"200 OK","payload":{"overallStatus":{"status":"LOAD_COMPLETED","totalRecords":10,"parsingErrors":0,"datatypeMismatchErrors":0,"insertErrors":0}}}"""));
        using var client = BuildClient(handler);

        var status = await client.GetLoadStatusAsync("abc-123");

        Assert.Equal(BulkLoadStatus.Completed, status.Status);
        Assert.False(status.HasRowErrors);
        Assert.Equal(10, status.TotalRecords);
    }

    [Fact]
    public async Task GetLoadStatusAsync_CompletedWithRowErrors_HasRowErrorsIsTrue()
    {
        var handler = new FakeHttpMessageHandler(_ => JsonResponse(
            """{"status":"200 OK","payload":{"overallStatus":{"status":"LOAD_COMPLETED","totalRecords":10,"parsingErrors":2,"datatypeMismatchErrors":0,"insertErrors":0}}}"""));
        using var client = BuildClient(handler);

        var status = await client.GetLoadStatusAsync("abc-123");

        Assert.Equal(BulkLoadStatus.Completed, status.Status);
        Assert.True(status.HasRowErrors);
    }

    [Fact]
    public async Task GetLoadStatusAsync_Failed_MapsToFailed_AndCapturesErrorMessages()
    {
        var handler = new FakeHttpMessageHandler(_ => JsonResponse(
            """{"status":"200 OK","payload":{"overallStatus":{"status":"LOAD_FAILED","totalRecords":0,"parsingErrors":0,"datatypeMismatchErrors":0,"insertErrors":0},"errors":{"errorLogs":[{"errorMessage":"boom"}]}}}"""));
        using var client = BuildClient(handler);

        var status = await client.GetLoadStatusAsync("abc-123");

        Assert.Equal(BulkLoadStatus.Failed, status.Status);
        Assert.Contains("boom", status.ErrorMessages);
    }

    [Fact]
    public async Task StartLoadAsync_OneTransientFailureThenSuccess_RetriesAndSucceeds()
    {
        var handler = new SequencedHttpMessageHandler(
            new HttpResponseMessage(HttpStatusCode.InternalServerError),
            StartLoadResponse("load-123"));
        using var client = BuildClient(handler);

        var result = await client.StartLoadAsync(new Uri("s3://bucket/prefix/"), "arn:aws:iam::123:role/x", "us-east-1");

        Assert.Equal("load-123", result.LoadId);
        Assert.Equal(2, handler.CallCount);
    }

    [Fact]
    public async Task GetLoadStatusAsync_OneTransientFailureThenSuccess_RetriesAndSucceeds()
    {
        var handler = new SequencedHttpMessageHandler(
            new HttpResponseMessage(HttpStatusCode.ServiceUnavailable),
            StatusResponse("LOAD_COMPLETED"));
        using var client = BuildClient(handler);

        var result = await client.GetLoadStatusAsync("load-123");

        Assert.Equal(BulkLoadStatus.Completed, result.Status);
        Assert.Equal(2, handler.CallCount);
    }

    [Fact]
    public async Task StartLoadAsync_AllAttemptsFailWith5xx_ThrowsAfterExhaustingRetries()
    {
        var handler = new SequencedHttpMessageHandler(
            new HttpResponseMessage(HttpStatusCode.InternalServerError),
            new HttpResponseMessage(HttpStatusCode.InternalServerError),
            new HttpResponseMessage(HttpStatusCode.InternalServerError));
        using var client = BuildClient(handler);

        await Assert.ThrowsAsync<HttpRequestException>(
            () => client.StartLoadAsync(new Uri("s3://bucket/prefix/"), "arn:aws:iam::123:role/x", "us-east-1"));

        Assert.Equal(3, handler.CallCount);
    }

    [Fact]
    public async Task StartLoadAsync_4xxResponse_ThrowsImmediatelyWithoutRetrying()
    {
        var handler = new SequencedHttpMessageHandler(new HttpResponseMessage(HttpStatusCode.BadRequest));
        using var client = BuildClient(handler);

        await Assert.ThrowsAsync<HttpRequestException>(
            () => client.StartLoadAsync(new Uri("s3://bucket/prefix/"), "arn:aws:iam::123:role/x", "us-east-1"));

        Assert.Equal(1, handler.CallCount);
    }
}

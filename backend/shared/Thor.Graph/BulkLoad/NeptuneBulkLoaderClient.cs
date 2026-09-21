using System.Net.Http.Json;

namespace Thor.Graph.BulkLoad;

/// <summary>
/// Calls Neptune's bulk loader REST API directly over HTTPS/HTTP, with every call signed via
/// IAM/SigV4 (see <see cref="NeptuneSigV4Signer"/>) by <see cref="SigV4SigningHandler"/>. The
/// request/response shapes here follow AWS's documented Neptune Loader API; this hasn't been
/// verified against a live Neptune endpoint, so confirm field names still match current AWS
/// docs before depending on this in production, particularly the error-detail shape read by
/// <see cref="GetLoadStatusAsync"/>.
/// </summary>
public sealed class NeptuneBulkLoaderClient : INeptuneBulkLoaderClient, IDisposable
{
    private readonly HttpClient _http;
    private readonly bool _ownsClient;

    public NeptuneBulkLoaderClient(NeptuneOptions options)
    {
        var scheme = options.EnableSsl ? "https" : "http";
        var signingHandler = new SigV4SigningHandler(options.Region) { InnerHandler = new HttpClientHandler() };
        _http = new HttpClient(signingHandler) { BaseAddress = new Uri($"{scheme}://{options.Endpoint}:{options.Port}/") };
        _ownsClient = true;
    }

    /// <summary>Test seam — lets tests substitute a fake handler instead of a real Neptune endpoint.</summary>
    internal NeptuneBulkLoaderClient(HttpClient httpClient)
    {
        _http = httpClient;
        _ownsClient = false;
    }

    public async Task<BulkLoadStartResult> StartLoadAsync(Uri s3SourceUri, string iamRoleArn, string region, CancellationToken cancellationToken = default)
    {
        var body = new StartLoadRequestBody(
            Source: s3SourceUri.ToString(), Format: "csv", IamRoleArn: iamRoleArn, Region: region,
            FailOnError: "FALSE", Parallelism: "MEDIUM");

        using var response = await SendWithRetryAsync(() => _http.PostAsJsonAsync("loader", body, cancellationToken), cancellationToken);
        response.EnsureSuccessStatusCode();

        var parsed = await response.Content.ReadFromJsonAsync<StartLoadResponse>(cancellationToken);
        var loadId = parsed?.Payload?.LoadId
            ?? throw new InvalidOperationException("Neptune bulk loader start response did not contain a loadId.");
        return new BulkLoadStartResult(loadId);
    }

    public async Task<BulkLoadStatusResult> GetLoadStatusAsync(string loadId, CancellationToken cancellationToken = default)
    {
        using var response = await SendWithRetryAsync(
            () => _http.GetAsync($"loader/{loadId}?details=true&errors=true", cancellationToken), cancellationToken);
        response.EnsureSuccessStatusCode();

        var parsed = await response.Content.ReadFromJsonAsync<LoadStatusResponse>(cancellationToken);
        var overall = parsed?.Payload?.OverallStatus
            ?? throw new InvalidOperationException("Neptune bulk loader status response did not contain an overallStatus.");

        var errorMessages = parsed.Payload?.Errors?.ErrorLogs?
            .Select(e => e.ErrorMessage)
            .Where(m => !string.IsNullOrEmpty(m))
            .Select(m => m!)
            .ToList() ?? [];

        return new BulkLoadStatusResult(
            loadId, MapStatus(overall.Status), overall.Status,
            overall.TotalRecords, overall.ParsingErrors, overall.DatatypeMismatchErrors, overall.InsertErrors,
            errorMessages);
    }

    private static BulkLoadStatus MapStatus(string rawStatus) => rawStatus switch
    {
        "LOAD_NOT_STARTED" or "LOAD_IN_QUEUE" or "LOAD_IN_PROGRESS" => BulkLoadStatus.InProgress,
        "LOAD_COMPLETED" => BulkLoadStatus.Completed,
        // Every other documented terminal status (LOAD_CANCELLED_*, LOAD_FAILED, LOAD_S3_*_ERROR,
        // LOAD_UNEXPECTED_ERROR, LOAD_DATA_DEADLOCK, etc.) is a failure/cancellation — treated the
        // same way here since none of them are recoverable by polling longer.
        _ => BulkLoadStatus.Failed,
    };

    /// <summary>
    /// Retries a Neptune loader HTTP call up to 3 total attempts on transient failures — a
    /// thrown <see cref="HttpRequestException"/> (the request never got a response at all) or a
    /// 5xx response. 4xx responses are not retried, since they're caller/config errors that
    /// won't self-resolve.
    /// </summary>
    private static async Task<HttpResponseMessage> SendWithRetryAsync(
        Func<Task<HttpResponseMessage>> send, CancellationToken cancellationToken)
    {
        const int maxAttempts = 3;
        for (var attempt = 1; ; attempt++)
        {
            HttpResponseMessage response;
            try
            {
                response = await send();
            }
            catch (HttpRequestException) when (attempt < maxAttempts)
            {
                await Task.Delay(BackoffDelay(attempt), cancellationToken);
                continue;
            }

            if (IsTransientFailure(response) && attempt < maxAttempts)
            {
                response.Dispose();
                await Task.Delay(BackoffDelay(attempt), cancellationToken);
                continue;
            }

            return response;
        }
    }

    private static TimeSpan BackoffDelay(int attempt) => TimeSpan.FromMilliseconds(200 * Math.Pow(2, attempt - 1));

    private static bool IsTransientFailure(HttpResponseMessage response) => (int)response.StatusCode is >= 500 and < 600;

    public void Dispose()
    {
        if (_ownsClient)
        {
            _http.Dispose();
        }
    }
}

/// <summary>Signs each outgoing request with SigV4 before it's sent, per Neptune's IAM auth requirement.</summary>
internal sealed class SigV4SigningHandler(string region) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        byte[]? body = null;
        string? contentType = null;
        if (request.Content is not null)
        {
            body = await request.Content.ReadAsByteArrayAsync(cancellationToken);
            contentType = request.Content.Headers.ContentType?.ToString();
        }

        var endpoint = new Uri($"{request.RequestUri!.Scheme}://{request.RequestUri.Authority}");
        var signedHeaders = NeptuneSigV4Signer.SignRequest(
            request.Method.Method, endpoint, request.RequestUri.PathAndQuery, region, body, contentType);

        foreach (var (name, value) in signedHeaders)
        {
            // Host is sent automatically from the request URI; Content-Type belongs on
            // HttpContent.Headers (already set there by the caller) rather than HttpRequestMessage.Headers.
            if (string.Equals(name, "host", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(name, "Content-Type", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            request.Headers.TryAddWithoutValidation(name, value);
        }

        return await base.SendAsync(request, cancellationToken);
    }
}

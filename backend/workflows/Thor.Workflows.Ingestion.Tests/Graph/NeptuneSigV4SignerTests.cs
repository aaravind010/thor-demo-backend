using Amazon.Runtime;
using Thor.Graph;
using Xunit;

namespace Thor.Workflows.Ingestion.Tests.Graph;

public sealed class NeptuneSigV4SignerTests
{
    private static readonly Uri Endpoint = new("https://my-cluster.us-east-1.neptune.amazonaws.com:8182");
    private const string Region = "us-east-1";

    [Fact]
    public void SignRequest_Get_ProducesWellFormedAuthorizationHeader()
    {
        var credentials = new BasicAWSCredentials("AKIDEXAMPLE", "secret");

        var headers = NeptuneSigV4Signer.SignRequest("GET", Endpoint, "/gremlin", Region, credentials);

        Assert.True(headers.ContainsKey("X-Amz-Date"));
        Assert.True(headers.ContainsKey("X-Amz-Content-SHA256"));
        var authorization = headers["Authorization"];
        Assert.StartsWith("AWS4-HMAC-SHA256 Credential=AKIDEXAMPLE/", authorization);
        Assert.Contains($"/{Region}/neptune-db/aws4_request,", authorization);
        Assert.Contains("SignedHeaders=host;x-amz-content-sha256;x-amz-date,", authorization);
        Assert.Contains("Signature=", authorization);
    }

    [Fact]
    public void SignRequest_BasicCredentials_DoesNotAddSecurityTokenHeader()
    {
        var credentials = new BasicAWSCredentials("AKIDEXAMPLE", "secret");

        var headers = NeptuneSigV4Signer.SignRequest("GET", Endpoint, "/gremlin", Region, credentials);

        Assert.False(headers.ContainsKey("X-Amz-Security-Token"));
    }

    [Fact]
    public void SignRequest_SessionCredentials_AddsSecurityTokenHeaderAndSignsIt()
    {
        var credentials = new SessionAWSCredentials("AKIDEXAMPLE", "secret", "the-session-token");

        var headers = NeptuneSigV4Signer.SignRequest("GET", Endpoint, "/gremlin", Region, credentials);

        Assert.Equal("the-session-token", headers["X-Amz-Security-Token"]);
        Assert.Contains("x-amz-security-token", headers["Authorization"]);
    }

    [Fact]
    public void SignRequest_PostWithBody_SignsContentTypeAndVariesContentHashWithBody()
    {
        var credentials = new BasicAWSCredentials("AKIDEXAMPLE", "secret");
        var body = System.Text.Encoding.UTF8.GetBytes("""{"source":"s3://bucket/x"}""");

        var emptyBodyHeaders = NeptuneSigV4Signer.SignRequest("POST", Endpoint, "/loader", Region, credentials, contentType: "application/json");
        var withBodyHeaders = NeptuneSigV4Signer.SignRequest("POST", Endpoint, "/loader", Region, credentials, body, "application/json");

        Assert.Equal("application/json", withBodyHeaders["Content-Type"]);
        Assert.Contains("content-type;host;x-amz-content-sha256;x-amz-date", withBodyHeaders["Authorization"]);
        Assert.NotEqual(emptyBodyHeaders["X-Amz-Content-SHA256"], withBodyHeaders["X-Amz-Content-SHA256"]);
    }

    [Fact]
    public void SignRequest_ResourcePathWithQueryString_Signs()
    {
        var credentials = new BasicAWSCredentials("AKIDEXAMPLE", "secret");

        var headers = NeptuneSigV4Signer.SignRequest(
            "GET", Endpoint, "/loader/abc-123?details=true&errors=true", Region, credentials);

        Assert.NotEmpty(headers["Authorization"]);
    }
}

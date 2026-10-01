using System.Security.Cryptography;
using System.Text;
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
    public void SignRequest_NoQueryString_MatchesIndependentSigV4Signature()
    {
        var credentials = new BasicAWSCredentials("AKIDEXAMPLE", "secret");

        var headers = NeptuneSigV4Signer.SignRequest("GET", Endpoint, "/loader/abc-123", Region, credentials);

        Assert.Equal(ExpectedSignature("GET", "/loader/abc-123", "", headers["X-Amz-Date"]), Signature(headers));
    }

    [Fact]
    public void SignRequest_ResourcePathWithQueryString_SignsQueryAsQueryNotPath()
    {
        var credentials = new BasicAWSCredentials("AKIDEXAMPLE", "secret");

        var headers = NeptuneSigV4Signer.SignRequest(
            "GET", Endpoint, "/loader/abc-123?errors=true&details=true", Region, credentials);

        Assert.Equal(
            ExpectedSignature("GET", "/loader/abc-123", "details=true&errors=true", headers["X-Amz-Date"]),
            Signature(headers));
    }

    [Fact]
    public void SignRequest_DeleteWithQueryString_SignsQueryAsQueryNotPath()
    {
        var credentials = new BasicAWSCredentials("AKIDEXAMPLE", "secret");

        var headers = NeptuneSigV4Signer.SignRequest("DELETE", Endpoint, "/loader?loadId=abc-123", Region, credentials);

        Assert.Equal(ExpectedSignature("DELETE", "/loader", "loadId=abc-123", headers["X-Amz-Date"]), Signature(headers));
    }

    private static string Signature(IReadOnlyDictionary<string, string> headers)
    {
        var authorization = headers["Authorization"];
        return authorization[(authorization.IndexOf("Signature=", StringComparison.Ordinal) + "Signature=".Length)..];
    }

    /// <summary>Independent SigV4 computation (empty body, no session token) so tests compare the actual signature, not just its presence.</summary>
    private static string ExpectedSignature(string method, string path, string canonicalQuery, string amzDate)
    {
        static byte[] Hmac(byte[] key, string data) => HMACSHA256.HashData(key, Encoding.UTF8.GetBytes(data));
        static string Hex(byte[] bytes) => Convert.ToHexString(bytes).ToLowerInvariant();

        var bodyHash = Hex(SHA256.HashData([]));
        var canonicalRequest =
            $"{method}\n{path}\n{canonicalQuery}\n" +
            $"host:{Endpoint.Authority}\nx-amz-content-sha256:{bodyHash}\nx-amz-date:{amzDate}\n\n" +
            $"host;x-amz-content-sha256;x-amz-date\n{bodyHash}";
        var scope = $"{amzDate[..8]}/{Region}/neptune-db/aws4_request";
        var stringToSign = $"AWS4-HMAC-SHA256\n{amzDate}\n{scope}\n{Hex(SHA256.HashData(Encoding.UTF8.GetBytes(canonicalRequest)))}";
        var signingKey = Hmac(Hmac(Hmac(Hmac(Encoding.UTF8.GetBytes("AWS4secret"), amzDate[..8]), Region), "neptune-db"), "aws4_request");
        return Hex(Hmac(signingKey, stringToSign));
    }
}

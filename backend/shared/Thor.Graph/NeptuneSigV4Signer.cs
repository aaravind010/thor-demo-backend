using Amazon;
using Amazon.Runtime;
using Amazon.Runtime.Credentials;
using Amazon.Runtime.Endpoints;
using Amazon.Runtime.Internal;
using Amazon.Runtime.Internal.Auth;
using Amazon.Runtime.Internal.Util;

namespace Thor.Graph;

/// <summary>
/// Signs Neptune Gremlin/bulk-loader requests with SigV4 for IAM database authentication,
/// against the "neptune-db" service. Credentials are resolved from the ambient AWS credential
/// chain (the same convention as <c>new AmazonS3Client()</c> elsewhere in this repo) fresh on
/// every call — never cached — so a rotated credential (e.g. an ECS task role) and a current
/// signature timestamp are always used.
/// </summary>
internal static class NeptuneSigV4Signer
{
    private const string ServiceName = "neptune-db";

    public static IReadOnlyDictionary<string, string> SignRequest(
        string httpMethod, Uri endpoint, string resourcePath, string region, byte[]? body = null, string? contentType = null)
    {
        var clientConfig = BuildClientConfig(region);
        var credentials = DefaultAWSCredentialsIdentityResolver.GetCredentials(clientConfig);
        return SignRequest(httpMethod, endpoint, resourcePath, region, credentials, body, contentType);
    }

    /// <summary>Test seam — lets tests sign with fixed/fake credentials instead of the ambient AWS credential chain.</summary>
    internal static IReadOnlyDictionary<string, string> SignRequest(
        string httpMethod, Uri endpoint, string resourcePath, string region, AWSCredentials credentials,
        byte[]? body = null, string? contentType = null)
    {
        var clientConfig = BuildClientConfig(region);
        var immutable = credentials.GetCredentials();

        var request = new DefaultRequest(new NeptuneSigningRequest(), ServiceName)
        {
            HttpMethod = httpMethod,
            Endpoint = endpoint,
            ResourcePath = resourcePath,
        };

        if (body is { Length: > 0 })
        {
            request.Content = body;
        }

        if (!string.IsNullOrEmpty(contentType))
        {
            request.Headers["Content-Type"] = contentType;
        }

        // AWS4Signer.Sign does not add this itself for temporary/session credentials — it must
        // already be present on the request so it's included in the canonical/signed headers.
        if (!string.IsNullOrEmpty(immutable.Token))
        {
            request.Headers["X-Amz-Security-Token"] = immutable.Token;
        }

        new AWS4Signer().Sign(request, clientConfig, new RequestMetrics(), credentials);

        return request.Headers.ToDictionary(kv => kv.Key, kv => kv.Value);
    }

    private static NeptuneSigningClientConfig BuildClientConfig(string region) => new()
    {
        RegionEndpoint = RegionEndpoint.GetBySystemName(region),
        AuthenticationServiceName = ServiceName,
        AuthenticationRegion = region,
    };

    /// <summary>Throwaway request instance — <see cref="DefaultRequest"/> requires one, but nothing here reads it.</summary>
    private sealed class NeptuneSigningRequest : AmazonWebServiceRequest;

    /// <summary>
    /// Minimal <see cref="ClientConfig"/> to satisfy <see cref="AWS4Signer.Sign"/>. Setting
    /// <see cref="ClientConfig.AuthenticationServiceName"/>/<see cref="ClientConfig.AuthenticationRegion"/>
    /// means the signer never needs endpoint-ruleset resolution, so
    /// <see cref="DetermineServiceOperationEndpoint"/> is unreachable here.
    /// </summary>
    private sealed class NeptuneSigningClientConfig : ClientConfig
    {
        public NeptuneSigningClientConfig() : base(new DefaultConfigurationProvider([new DefaultConfiguration()]))
        {
        }

        public override string ServiceVersion => "1";

        public override string UserAgent => "Thor.Graph";

        public override string RegionEndpointServiceName => ServiceName;

        public override Endpoint DetermineServiceOperationEndpoint(ServiceOperationEndpointParameters parameters) =>
            throw new NotSupportedException($"{nameof(NeptuneSigningClientConfig)} always sets an explicit signing region/service and should never need endpoint-ruleset resolution.");
    }
}

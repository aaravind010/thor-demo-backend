namespace Thor.Authorizer.Core;

public sealed record ClassificationResult(CredentialType Type, string? Material)
{
    public static ClassificationResult ForApiKey(string material) => new(CredentialType.ApiKey, material);
    public static ClassificationResult ForJwt(string material) => new(CredentialType.Jwt, material);
    public static ClassificationResult Unrecognized() => new(CredentialType.Unknown, null);
}

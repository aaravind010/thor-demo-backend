namespace Thor.Authorizer.Core.Auth.ApiKey;

public interface IApiKeyHasher
{
    /// <summary>Produces a self-describing encoded hash string for storage.</summary>
    Task<string> HashAsync(string secret);

    /// <summary>Constant-time verification of a plaintext secret against a stored encoded hash.</summary>
    Task<bool> VerifyAsync(string secret, string encodedHash);
}

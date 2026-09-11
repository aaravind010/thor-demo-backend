using Thor.Authorizer.Core.Auth.ApiKey;
using Thor.Authorizer.Core.DataAccess;

namespace Thor.Authorizer.Test.TestFixtures;

public static class ApiKeyRecordFixtures
{
    // Blocking here is safe: the substituted salt provider resolves synchronously and PBKDF2
    // is pure CPU work, so there's no real async I/O to deadlock on — keeps these fixtures usable
    // from synchronous test setup without pushing async through every caller.
    private static readonly Pbkdf2ApiKeyHasher Hasher = new(TestSaltProvider.Create());

    private static string Hash(string secret) => Hasher.HashAsync(secret).GetAwaiter().GetResult();

    public static ApiKeyRecord Active(string tenantId = "tenant-1", string keyId = "key-1", string secret = "s3cr3t", string principalId = "principal-1") =>
        new(keyId, tenantId, Hash(secret), "active", ExpiresAt: null, principalId);

    public static ApiKeyRecord Expired(string tenantId = "tenant-1", string keyId = "key-1", string secret = "s3cr3t", string principalId = "principal-1") =>
        new(keyId, tenantId, Hash(secret), "active", ExpiresAt: DateTimeOffset.UtcNow.AddDays(-1), principalId);

    public static ApiKeyRecord Revoked(string tenantId = "tenant-1", string keyId = "key-1", string secret = "s3cr3t", string principalId = "principal-1") =>
        new(keyId, tenantId, Hash(secret), "revoked", ExpiresAt: null, principalId);
}

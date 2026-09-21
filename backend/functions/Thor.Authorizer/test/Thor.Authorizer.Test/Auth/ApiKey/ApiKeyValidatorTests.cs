using FluentAssertions;
using Thor.Authorizer.Core.Auth.ApiKey;
using Thor.Authorizer.Core.DataAccess;
using Thor.Authorizer.Core.DataAccess.Fakes;
using Thor.Authorizer.Test.TestFixtures;

namespace Thor.Authorizer.Test.Auth.ApiKey;

public class ApiKeyValidatorTests
{
    private const string Secret = "s3cr3t";

    // Uses the real system clock (not FakeTimeProvider) since these tests assert expiry
    // relative to real DateTimeOffset.UtcNow set on the fixtures, not a controllable instant.
    // Uses TestSaltProvider so hashes created by ApiKeyRecordFixtures (same salt) verify.
    private static ApiKeyValidator CreateValidator(IApiKeyRepository repository, TimeProvider? timeProvider = null) =>
        new(repository, new Pbkdf2ApiKeyHasher(TestSaltProvider.Create()), timeProvider ?? TimeProvider.System);

    [Fact]
    public async Task ValidateAsync_SameKeyIdUnderDifferentTenants_ResolvesIndependently()
    {
        var tenantAKey = ApiKeyRecordFixtures.Active(tenantId: "tenant-a", keyId: "key-1", secret: Secret, principalId: "principal-a");
        var tenantBKey = ApiKeyRecordFixtures.Active(tenantId: "tenant-b", keyId: "key-1", secret: Secret, principalId: "principal-b");
        var repository = new InMemoryApiKeyRepository([tenantAKey, tenantBKey]);
        var validator = CreateValidator(repository);

        var resultA = await validator.ValidateAsync("tenant-a", $"key-1.{Secret}");
        var resultB = await validator.ValidateAsync("tenant-b", $"key-1.{Secret}");

        resultA.IsValid.Should().BeTrue();
        resultA.PrincipalId.Should().Be("principal-a");
        resultB.IsValid.Should().BeTrue();
        resultB.PrincipalId.Should().Be("principal-b");
    }

    [Fact]
    public async Task ValidateAsync_CorrectKeyButWrongTenant_FailsAsNotFound()
    {
        var key = ApiKeyRecordFixtures.Active(tenantId: "tenant-a", keyId: "key-1", secret: Secret);
        var repository = new InMemoryApiKeyRepository([key]);
        var validator = CreateValidator(repository);

        var result = await validator.ValidateAsync("tenant-b", $"key-1.{Secret}");

        result.IsValid.Should().BeFalse();
        result.FailureReason.Should().Be("key not found for tenant");
    }

    [Fact]
    public async Task ValidateAsync_ExpiredKey_FailsEvenWithCorrectSecret()
    {
        var key = ApiKeyRecordFixtures.Expired(secret: Secret);
        var repository = new InMemoryApiKeyRepository([key]);
        var validator = CreateValidator(repository);

        var result = await validator.ValidateAsync(key.TenantId, $"{key.KeyId}.{Secret}");

        result.IsValid.Should().BeFalse();
        result.FailureReason.Should().Be("key expired");
    }

    [Fact]
    public async Task ValidateAsync_RevokedKey_Fails()
    {
        var key = ApiKeyRecordFixtures.Revoked(secret: Secret);
        var repository = new InMemoryApiKeyRepository([key]);
        var validator = CreateValidator(repository);

        var result = await validator.ValidateAsync(key.TenantId, $"{key.KeyId}.{Secret}");

        result.IsValid.Should().BeFalse();
        result.FailureReason.Should().Be("key not active");
    }

    [Fact]
    public async Task ValidateAsync_WrongSecret_Fails()
    {
        var key = ApiKeyRecordFixtures.Active(secret: Secret);
        var repository = new InMemoryApiKeyRepository([key]);
        var validator = CreateValidator(repository);

        var result = await validator.ValidateAsync(key.TenantId, $"{key.KeyId}.wrong-secret");

        result.IsValid.Should().BeFalse();
        result.FailureReason.Should().Be("secret mismatch");
    }

    [Theory]
    [InlineData("no-dot-here")]
    [InlineData(".missing-key-id")]
    [InlineData("missing-secret.")]
    public async Task ValidateAsync_MalformedKeyMaterial_Fails(string material)
    {
        var repository = new InMemoryApiKeyRepository([]);
        var validator = CreateValidator(repository);

        var result = await validator.ValidateAsync("tenant-1", material);

        result.IsValid.Should().BeFalse();
        result.FailureReason.Should().Be("malformed api key material");
    }
}

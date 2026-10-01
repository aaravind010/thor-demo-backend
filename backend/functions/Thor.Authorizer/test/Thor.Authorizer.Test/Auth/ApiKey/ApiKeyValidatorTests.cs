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
    public async Task ValidateAsync_ValidKey_ReturnsTenantAndPrincipalFromStoredRecord()
    {
        var key = ApiKeyRecordFixtures.Active(tenantId: "tenant-a", keyId: "key-1", secret: Secret, principalId: "principal-a");
        var repository = new InMemoryApiKeyRepository([key]);
        var validator = CreateValidator(repository);

        var result = await validator.ValidateAsync($"key-1.{Secret}");

        result.IsValid.Should().BeTrue();
        result.TenantId.Should().Be("tenant-a");
        result.PrincipalId.Should().Be("principal-a");
    }

    [Fact]
    public async Task ValidateAsync_UnknownKeyId_FailsAsNotFound()
    {
        var key = ApiKeyRecordFixtures.Active(keyId: "key-1", secret: Secret);
        var repository = new InMemoryApiKeyRepository([key]);
        var validator = CreateValidator(repository);

        var result = await validator.ValidateAsync($"key-2.{Secret}");

        result.IsValid.Should().BeFalse();
        result.FailureReason.Should().Be("key not found");
    }

    [Fact]
    public async Task ValidateAsync_ExpiredKey_FailsEvenWithCorrectSecret()
    {
        var key = ApiKeyRecordFixtures.Expired(secret: Secret);
        var repository = new InMemoryApiKeyRepository([key]);
        var validator = CreateValidator(repository);

        var result = await validator.ValidateAsync($"{key.KeyId}.{Secret}");

        result.IsValid.Should().BeFalse();
        result.FailureReason.Should().Be("key expired");
    }

    [Fact]
    public async Task ValidateAsync_RevokedKey_Fails()
    {
        var key = ApiKeyRecordFixtures.Revoked(secret: Secret);
        var repository = new InMemoryApiKeyRepository([key]);
        var validator = CreateValidator(repository);

        var result = await validator.ValidateAsync($"{key.KeyId}.{Secret}");

        result.IsValid.Should().BeFalse();
        result.FailureReason.Should().Be("key not active");
    }

    [Fact]
    public async Task ValidateAsync_WrongSecret_Fails()
    {
        var key = ApiKeyRecordFixtures.Active(secret: Secret);
        var repository = new InMemoryApiKeyRepository([key]);
        var validator = CreateValidator(repository);

        var result = await validator.ValidateAsync($"{key.KeyId}.wrong-secret");

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

        var result = await validator.ValidateAsync(material);

        result.IsValid.Should().BeFalse();
        result.FailureReason.Should().Be("malformed api key material");
    }
}

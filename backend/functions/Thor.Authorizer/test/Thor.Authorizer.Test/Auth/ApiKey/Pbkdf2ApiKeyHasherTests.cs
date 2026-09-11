using FluentAssertions;
using NSubstitute;
using Thor.Authorizer.Core.Auth.ApiKey;
using Thor.Authorizer.Core.Auth.Exceptions;
using Thor.Authorizer.Test.TestFixtures;

namespace Thor.Authorizer.Test.Auth.ApiKey;

public class Pbkdf2ApiKeyHasherTests
{
    private readonly Pbkdf2ApiKeyHasher _hasher = new(TestSaltProvider.Create());

    [Fact]
    public async Task HashThenVerify_WithCorrectSecret_Succeeds()
    {
        var hash = await _hasher.HashAsync("correct-secret");

        (await _hasher.VerifyAsync("correct-secret", hash)).Should().BeTrue();
    }

    [Fact]
    public async Task Verify_WithWrongSecret_Fails()
    {
        var hash = await _hasher.HashAsync("correct-secret");

        (await _hasher.VerifyAsync("wrong-secret", hash)).Should().BeFalse();
    }

    [Fact]
    public async Task Verify_WithTamperedHashString_FailsClosedWithoutThrowing()
    {
        var act = async () => await _hasher.VerifyAsync("correct-secret", "not-a-valid-hash-format");

        await act.Should().NotThrowAsync();
        (await _hasher.VerifyAsync("correct-secret", "not-a-valid-hash-format")).Should().BeFalse();
    }

    // Documents the confirmed trade-off of a shared salt: since salt, iterations, and secret are
    // all identical across calls, PBKDF2 is deterministic — two keys (or two hashes of the same
    // secret) are no longer guaranteed to differ the way they did with a per-key random salt.
    [Fact]
    public async Task Hash_CalledTwiceForSameSecret_ProducesIdenticalHash_BecauseSaltIsShared()
    {
        var hash1 = await _hasher.HashAsync("same-secret");
        var hash2 = await _hasher.HashAsync("same-secret");

        hash1.Should().Be(hash2);
    }

    [Fact]
    public async Task Verify_WithDifferentSalt_Fails()
    {
        var hash = await _hasher.HashAsync("correct-secret");

        var otherSaltProvider = Substitute.For<ISaltProvider>();
        otherSaltProvider.GetSaltAsync().Returns("a-completely-different-salt"u8.ToArray());
        var hasherWithDifferentSalt = new Pbkdf2ApiKeyHasher(otherSaltProvider);

        (await hasherWithDifferentSalt.VerifyAsync("correct-secret", hash)).Should().BeFalse();
    }

    [Fact]
    public async Task VerifyAsync_WhenSaltProviderThrows_PropagatesRatherThanSilentlySkippingSalt()
    {
        var throwingSaltProvider = Substitute.For<ISaltProvider>();
        throwingSaltProvider.GetSaltAsync().Returns<Task<byte[]>>(_ => throw new SaltUnavailableException("boom"));
        var hasher = new Pbkdf2ApiKeyHasher(throwingSaltProvider);

        var act = async () => await hasher.VerifyAsync("correct-secret", "PBKDF2-SHA256$210000$AAAA");

        await act.Should().ThrowAsync<SaltUnavailableException>();
    }
}

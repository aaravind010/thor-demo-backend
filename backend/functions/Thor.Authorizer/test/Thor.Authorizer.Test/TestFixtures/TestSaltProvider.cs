using NSubstitute;
using Thor.Authorizer.Core.Auth.ApiKey;

namespace Thor.Authorizer.Test.TestFixtures;

/// <summary>Shared fixed salt for tests — hashes created via one instance must verify against
/// another instance, so every test that needs a salt uses this same value.</summary>
public static class TestSaltProvider
{
    public static readonly byte[] Value = "test-salt-do-not-use-in-production"u8.ToArray();

    public static ISaltProvider Create()
    {
        var provider = Substitute.For<ISaltProvider>();
        provider.GetSaltAsync().Returns(Value);
        return provider;
    }
}

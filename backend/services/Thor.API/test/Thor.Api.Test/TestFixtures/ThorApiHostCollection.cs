namespace Thor.Api.Test.TestFixtures;

/// <summary>
/// Serializes test classes that boot Thor.Api in-process. Program.cs configures Serilog's global
/// bootstrap logger (<c>Log.Logger</c>) at startup, so two hosts starting concurrently race on it
/// and one exits without building a host ("The entry point exited without ever building an IHost").
/// </summary>
[CollectionDefinition(Name)]
public sealed class ThorApiHostCollection
{
    public const string Name = "Thor.Api in-process host";
}

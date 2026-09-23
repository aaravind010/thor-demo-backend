namespace Thor.Api.Utils;

/// <summary>
/// Reads required environment variables for service startup, failing fast when one is unset
/// rather than silently falling back to a dev-only default (ADR §5/§6 — never trust an absent
/// or spoofable config value).
/// </summary>
public static class RequiredEnvironment
{
    public static string GetVariable(string name)
    {
        var value = Environment.GetEnvironmentVariable(name);
        if (string.IsNullOrEmpty(value))
        {
            throw new InvalidOperationException($"Required environment variable '{name}' is not set. Value: '{value}'");
        }

        return value;
    }
}

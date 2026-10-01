namespace Thor.TaskApi.Utils;

public static class EnvironmentVariableHelper
{
    public static string RequireEnvironmentVariable(string name) =>
        Environment.GetEnvironmentVariable(name) is { Length: > 0 } value
            ? value
            : throw new InvalidOperationException($"Required environment variable '{name}' is not set.");
}

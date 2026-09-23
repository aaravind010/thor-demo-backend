namespace Thor.Authorizer.Core;

/// <summary>
/// TEMPORARY: routes the authorizer allows without verifying any credential (JWT or API key) —
/// see AuthorizerHandler's use of this. Kept here, in the Lambda's own code, rather than as an
/// API Gateway route config that bypasses the Lambda entirely, so there's one place that decides
/// what's exempt and exempt routes still go through (best-effort) Host-based tenant resolution.
/// </summary>
public static class ExemptRoutes
{
    public static bool SkipsCredentialValidation(string? httpMethod, string? path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return false;
        }

        var normalized = path.Length > 1 ? path.TrimEnd('/') : path;

        if (string.Equals(normalized, "/health", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (string.Equals(normalized, "/scalar", StringComparison.OrdinalIgnoreCase) ||
            normalized.StartsWith("/scalar/", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        // POST /v{version}/connector-api-keys — TEMPORARY, see ConnectorApiKeyService's own
        // TEMPORARY remarks in Thor.Api for why, and what should change once this route goes
        // back behind full credential verification.
        return string.Equals(httpMethod, "POST", StringComparison.OrdinalIgnoreCase) &&
            IsConnectorApiKeysRoute(normalized);
    }

    private static bool IsConnectorApiKeysRoute(string path)
    {
        var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);

        return segments.Length == 2
            && IsVersionSegment(segments[0])
            && string.Equals(segments[1], "connector-api-keys", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsVersionSegment(string segment) =>
        segment.Length > 1 && segment[0] is 'v' or 'V' && segment[1..].All(char.IsDigit);
}

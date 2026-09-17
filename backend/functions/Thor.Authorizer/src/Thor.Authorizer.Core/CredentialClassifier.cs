namespace Thor.Authorizer.Core;

/// <summary>
/// Classifies the Authorization header as an API key (Authorization: ApiKey {key_id}.{secret})
/// or a Cognito JWT (Authorization: [Bearer ]{header}.{payload}.{signature}). Anything else,
/// including an empty header, is Unknown and must be denied — never "try both".
/// </summary>
public static class CredentialClassifier
{
    private const string ApiKeyScheme = "ApiKey ";
    private const string BearerScheme = "Bearer ";

    public static ClassificationResult Classify(string? authorizationHeader)
    {
        if (string.IsNullOrWhiteSpace(authorizationHeader))
        {
            return ClassificationResult.Unrecognized();
        }

        var trimmed = authorizationHeader.Trim();

        if (trimmed.StartsWith(ApiKeyScheme, StringComparison.Ordinal))
        {
            var material = trimmed[ApiKeyScheme.Length..].Trim();
            return material.Length > 0
                ? ClassificationResult.ForApiKey(material)
                : ClassificationResult.Unrecognized();
        }

        var candidate = trimmed.StartsWith(BearerScheme, StringComparison.OrdinalIgnoreCase)
            ? trimmed[BearerScheme.Length..].Trim()
            : trimmed;

        return LooksLikeCompactJwt(candidate)
            ? ClassificationResult.ForJwt(candidate)
            : ClassificationResult.Unrecognized();
    }

    private static bool LooksLikeCompactJwt(string value)
    {
        var parts = value.Split('.');
        return parts.Length == 3 && parts.All(p => p.Length > 0 && IsBase64Url(p));
    }

    private static bool IsBase64Url(string segment) =>
        segment.All(c => char.IsLetterOrDigit(c) || c is '-' or '_');
}

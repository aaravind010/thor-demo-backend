namespace Thor.Authorizer.Core.Tenancy;

public static class HostParser
{
    /// <summary>
    /// Extracts the leftmost label of a Host header as the tenant subdomain, e.g.
    /// "acme.api.thor.example.com" -> "acme". Returns null if the host has too few
    /// labels to contain a tenant subdomain or is malformed.
    /// </summary>
    public static string? ExtractSubdomain(string? hostHeader, int minLabelsForTenantDomain = 3)
    {
        if (string.IsNullOrWhiteSpace(hostHeader))
        {
            return null;
        }

        var hostOnly = hostHeader.Split(':')[0].Trim();
        var labels = hostOnly.Split('.', StringSplitOptions.RemoveEmptyEntries);

        if (labels.Length < minLabelsForTenantDomain)
        {
            return null;
        }

        return labels[0].ToLowerInvariant();
    }
}

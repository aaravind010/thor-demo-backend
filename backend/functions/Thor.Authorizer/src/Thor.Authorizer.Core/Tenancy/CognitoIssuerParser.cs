using System.Text.RegularExpressions;

namespace Thor.Authorizer.Core.Tenancy;

/// <summary>
/// The (region, user pool id) a Cognito <c>iss</c> claim names.
/// </summary>
public sealed record CognitoIssuer(string Region, string UserPoolId);

/// <summary>
/// Parses an unverified Cognito issuer (<c>https://cognito-idp.{region}.amazonaws.com/{region}_{id}</c>)
/// into its region and user pool id. Strict on purpose: anything else is rejected before it can
/// become a tenant-routing lookup key, so arbitrary <c>iss</c> values never reach the DB or fill
/// the negative cache. The result is a lookup hint only — the token is still fully verified
/// against the resolved tenant's pool.
/// </summary>
public static partial class CognitoIssuerParser
{
    [GeneratedRegex(
        @"^https://cognito-idp\.(?<region>[a-z]{2}(?:-[a-z]+)+-\d)\.amazonaws\.com/(?<pool>\k<region>_[A-Za-z0-9]+)\z",
        RegexOptions.CultureInvariant)]
    private static partial Regex IssuerPattern();

    public static CognitoIssuer? TryParse(string? issuer)
    {
        if (string.IsNullOrEmpty(issuer))
        {
            return null;
        }

        var match = IssuerPattern().Match(issuer);
        return match.Success
            ? new CognitoIssuer(match.Groups["region"].Value, match.Groups["pool"].Value)
            : null;
    }
}

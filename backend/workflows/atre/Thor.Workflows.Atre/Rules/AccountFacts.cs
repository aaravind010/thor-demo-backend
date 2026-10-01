using System.Text.Json;
using Thor.DataLayer.Models.Tenants;

namespace Thor.Workflows.Atre.Rules;

/// <summary>
/// Resolves a rule's <c>field</c> against an account: known top-level columns first, falling back
/// to <c>raw_attributes[field]</c> (a JSON object stored as plain text — see
/// <c>OwnerRefsSql</c>'s note that no C# model has a <c>jsonb</c> column annotation in this repo)
/// — mirrors the POC's flattened-dict-then-raw_attributes lookup order exactly. The caller
/// (<see cref="RuleEvaluator"/>) lowercases the result before comparing; this only resolves the
/// raw string, or <c>null</c> if the field isn't found anywhere.
/// </summary>
internal static class AccountFacts
{
    public static string? Resolve(Account account, string field)
    {
        var known = field switch
        {
            "account_kind" => account.AccountKind,
            "is_human" => account.IsHuman ? "true" : "false",
            "display_name" => account.DisplayName,
            "upn" => account.Upn,
            "email" => account.Email,
            "domain_name" => account.DomainName,
            _ => null,
        };

        return !string.IsNullOrEmpty(known) ? known : ResolveFromRawAttributes(account.RawAttributes, field);
    }

    private static string? ResolveFromRawAttributes(string? rawAttributesJson, string field)
    {
        if (string.IsNullOrWhiteSpace(rawAttributesJson))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(rawAttributesJson);
            if (document.RootElement.ValueKind != JsonValueKind.Object ||
                !document.RootElement.TryGetProperty(field, out var value))
            {
                return null;
            }

            return value.ValueKind switch
            {
                JsonValueKind.String => value.GetString(),
                JsonValueKind.True => "true",
                JsonValueKind.False => "false",
                JsonValueKind.Number => value.GetRawText(),
                _ => null,
            };
        }
        catch (JsonException)
        {
            // Malformed raw_attributes must never abort a run — treated as field-not-found.
            return null;
        }
    }
}

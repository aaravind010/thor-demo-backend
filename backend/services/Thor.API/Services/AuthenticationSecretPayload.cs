using System.Text.Json.Serialization;

namespace Thor.Api.Services;

/// <summary>
/// Shape of the JSON payload stored in the per-tenant-per-authentication-type Secrets Manager
/// secret: one entry per <see cref="Thor.DataLayer.Models.Tenants.AuthenticationValue"/> row,
/// keyed by that row's own Id.
/// </summary>
internal sealed class AuthenticationSecretPayload
{
    [JsonPropertyName("values")]
    public Dictionary<string, string> Values { get; set; } = new();
}

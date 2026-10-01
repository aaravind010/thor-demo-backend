using System.Text.Json.Serialization;

namespace Thor.DataConnectionManager.Secrets;

/// <summary>
/// Shape of the JSON payload stored in the per-tenant-per-authentication-type Secrets Manager
/// secret: one entry per <see cref="Thor.DataLayer.Models.Tenants.AuthenticationValue"/> row,
/// keyed by that row's own Id. Shared by the writer (Thor.Api, on authentication-method
/// creation) and the reader (Thor.TaskApi, on <c>GET /tasks/{taskId}/settings</c>) so both
/// agree on one wire format.
/// </summary>
public sealed class AuthenticationSecretPayload
{
    [JsonPropertyName("values")]
    public Dictionary<string, string> Values { get; set; } = new();
}

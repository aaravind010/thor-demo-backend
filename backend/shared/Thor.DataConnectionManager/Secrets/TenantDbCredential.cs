using System.Text.Json.Serialization;

namespace Thor.DataConnectionManager.Secrets;

/// <summary>
/// Shape of the JSON payload AWS Secrets Manager returns for an RDS-managed secret.
/// </summary>
internal sealed class TenantDbCredential
{
    [JsonPropertyName("username")]
    public string Username { get; set; } = null!;

    [JsonPropertyName("password")]
    public string Password { get; set; } = null!;
}

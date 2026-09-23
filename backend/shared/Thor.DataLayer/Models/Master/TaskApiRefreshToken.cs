using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Thor.DataLayer.Models;

/// <summary>
/// A refresh-token session issued to a connector alongside a Task API JWT (see
/// <c>POST /register</c> in Thor.TaskApi). Not part of the tenant metadata ERD — introduced so
/// <c>POST /register/refresh</c> can validate and single-use-rotate refresh tokens without
/// them being bare stateless JWTs. The raw token presented by a connector has the form
/// <c>thor_rt_{TokenId}_{secret}</c>; only the salted HMAC hash of <c>secret</c> is stored.
/// </summary>
[Table("task_api_refresh_token", Schema = "auth")]
public sealed class TaskApiRefreshToken
{
    [Key]
    [Column("token_id")]
    public Guid TokenId { get; set; }

    [Column("key_id")]
    [ForeignKey(nameof(ApiKey))]
    public Guid KeyId { get; set; }

    [Column("token_hash")]
    public string TokenHash { get; set; } = null!;

    [Column("salt")]
    public string Salt { get; set; } = null!;

    [Column("role_type")]
    public string RoleType { get; set; } = null!;

    [Column("created_at")]
    public DateTimeOffset CreatedAt { get; set; }

    [Column("expires_at")]
    public DateTimeOffset ExpiresAt { get; set; }

    [Column("revoked_at")]
    public DateTimeOffset? RevokedAt { get; set; }

    public TenantApiKey ApiKey { get; set; } = null!;
}

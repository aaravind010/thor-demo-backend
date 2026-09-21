using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Thor.DataLayer.Models;

/// <summary>
/// Well-known values for <see cref="TenantApiKey.StatusId"/>. Plain constants rather than a
/// lookup table/entity, matching how <see cref="Tenant"/>'s own status-ish columns
/// (<c>tier_id</c>, <c>isolation_type_id</c>, <c>status_id</c>) are modeled in this codebase today.
/// </summary>
public static class ApiKeyStatus
{
    public const short Active = 1;
    public const short Revoked = 2;
    public const short Expired = 3;
}

/// <summary>
/// A connector API-key credential for a tenant (ADR §5.2) — lives in the Master metadata DB,
/// not the tenant DB, so verifying a key never opens a per-tenant connection. The raw key
/// presented by a connector has the form <c>thor_{KeyId}_{secret}</c>; only the salted HMAC
/// hash of <c>secret</c> is stored, never the secret itself.
/// </summary>
[Table("tenant_api_key", Schema = "auth")]
public sealed class TenantApiKey
{
    [Key]
    [Column("key_id")]
    public Guid KeyId { get; set; }

    [Column("tenant_id")]
    [ForeignKey(nameof(Tenant))]
    public Guid TenantId { get; set; }

    [Column("secret_hash")]
    public string SecretHash { get; set; } = null!;

    [Column("salt")]
    public string Salt { get; set; } = null!;

    [Column("label")]
    public string? Label { get; set; }

    [Column("status_id")]
    public short StatusId { get; set; }

    [Column("created_at")]
    public DateTimeOffset CreatedAt { get; set; }

    [Column("expires_at")]
    public DateTimeOffset? ExpiresAt { get; set; }

    [Column("last_used_at")]
    public DateTimeOffset? LastUsedAt { get; set; }

    public Tenant Tenant { get; set; } = null!;

    public ICollection<KeyScopeMap> ScopeMaps { get; set; } = new List<KeyScopeMap>();
}

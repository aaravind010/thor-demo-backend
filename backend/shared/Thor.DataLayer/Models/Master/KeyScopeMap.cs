using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Thor.DataLayer.Models;

/// <summary>
/// Join row granting one <see cref="ApiScope"/> to one <see cref="TenantApiKey"/>. A key's
/// granted scopes are the set of <see cref="ApiScope.ScopeText"/> values reachable through its
/// <c>KeyScopeMap</c> rows.
/// </summary>
[Table("key_scope_map", Schema = "auth")]
public sealed class KeyScopeMap
{
    [Key]
    [Column("id")]
    public short Id { get; set; }

    [Column("api_key_uuid")]
    [ForeignKey(nameof(ApiKey))]
    public Guid ApiKeyUuid { get; set; }

    [Column("scope_id")]
    [ForeignKey(nameof(Scope))]
    public short ScopeId { get; set; }

    public TenantApiKey ApiKey { get; set; } = null!;

    public ApiScope Scope { get; set; } = null!;
}

using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Thor.DataLayer.Models;

/// <summary>
/// A grantable scope/role (e.g. "scanner") that a <see cref="TenantApiKey"/> can carry, via
/// <see cref="KeyScopeMap"/>. Lives in the Master metadata DB — shared reference data, not
/// per-tenant.
/// </summary>
[Table("api_scopes", Schema = "auth")]
public sealed class ApiScope
{
    [Key]
    [Column("id")]
    public short Id { get; set; }

    [Column("scope_text")]
    public string ScopeText { get; set; } = null!;
}

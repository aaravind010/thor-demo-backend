using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Thor.DataLayer.Models.Tenants;

/// <summary>
/// A privileged-access-evaluation result: whether a given account has a privileged
/// access path to a target (asset or entitlement, identified polymorphically by
/// <see cref="TargetType"/>/<see cref="TargetId"/>) matching a
/// <see cref="PrivilegeDefinition"/>.
/// </summary>
[Table("pai_result")]
public sealed class PaiResult
{
    [Key]
    [Column("id")]
    public Guid Id { get; set; }

    [Column("account_id")]
    [ForeignKey(nameof(Account))]
    public Guid AccountId { get; set; }

    [Column("target_id")]
    public Guid TargetId { get; set; }

    [Column("target_type")]
    public string TargetType { get; set; } = null!;

    [Column("access_path")]
    public string AccessPath { get; set; } = null!;

    [Column("privilege_definition_id")]
    [ForeignKey(nameof(PrivilegeDefinition))]
    public Guid PrivilegeDefinitionId { get; set; }

    [Column("is_privileged")]
    public bool IsPrivileged { get; set; }

    [Column("is_active")]
    public bool IsActive { get; set; }

    [Column("computed_at")]
    public DateTimeOffset ComputedAt { get; set; }

    public Account Account { get; set; } = null!;

    public PrivilegeDefinition PrivilegeDefinition { get; set; } = null!;
}

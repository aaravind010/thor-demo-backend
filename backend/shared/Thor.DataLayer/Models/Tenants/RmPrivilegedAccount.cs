using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations.Schema;

namespace Thor.DataLayer.Models.Tenants;

/// <summary>
/// A read-model row summarizing a privileged-access finding for an <see cref="Account"/>
/// as of its <see cref="LastScan"/>. Maps to the "rm_privileged_accounts" table; the
/// class is singular ("RmPrivilegedAccount") since one row represents one account's
/// privileged-access entry.
/// </summary>
[Table("rm_privileged_accounts")]
[PrimaryKey(nameof(AccountId), nameof(PrivilegeDefinitionId))]
public sealed class RmPrivilegedAccount
{
    [Column("account_id")]
    public Guid AccountId { get; set; }

    [Column("privilege_definition_id")]
    public Guid PrivilegeDefinitionId { get; set; }

    [Column("display_name")]
    public string DisplayName { get; set; } = null!;

    [Column("account_kind")]
    public string AccountKind { get; set; } = null!;

    [Column("privilege_name")]
    public string PrivilegeName { get; set; } = null!;

    [Column("severity")]
    public string Severity { get; set; } = null!;

    [Column("access_path")]
    public string AccessPath { get; set; } = null!;

    [Column("computed_at")]
    public DateTimeOffset ComputedAt { get; set; }

    [Column("last_scan_id")]
    [ForeignKey(nameof(LastScan))]
    public Guid LastScanId { get; set; }

    [Column("last_refreshed_at")]
    public DateTimeOffset LastRefreshedAt { get; set; }

    public Account Account { get; set; } = null!;

    public PrivilegeDefinition PrivilegeDefinition { get; set; } = null!;

    public Scan LastScan { get; set; } = null!;
}

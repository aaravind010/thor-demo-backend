using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Thor.DataLayer.Models.Tenants;

/// <summary>
/// A classification an account/group can be assigned (e.g. service account, shared
/// mailbox, human user) via <see cref="AccountTypeAssignment"/>.
/// </summary>
[Table("account_type")]
public sealed class AccountType
{
    [Key]
    [Column("id")]
    public Guid Id { get; set; }

    [Column("name")]
    public string Name { get; set; } = null!;

    [Column("description")]
    public string Description { get; set; } = null!;

    [Column("is_human")]
    public bool IsHuman { get; set; }

    private readonly List<AccountTypeRule> _accountTypeRules = new();
    public IEnumerable<AccountTypeRule> AccountTypeRules => _accountTypeRules;

    private readonly List<AccountTypeAssignment> _accountTypeAssignments = new();
    public IEnumerable<AccountTypeAssignment> AccountTypeAssignments => _accountTypeAssignments;
}

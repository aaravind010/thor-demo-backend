using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Thor.DataLayer.Models.Tenants;

/// <summary>
/// A compliance/security control definition that <see cref="Violation"/> rows are
/// raised against.
/// </summary>
[Table("control_rule")]
public sealed class ControlRule
{
    [Key]
    [Column("id")]
    public Guid Id { get; set; }

    [Column("rule_name")]
    public string RuleName { get; set; } = null!;

    [Column("rule_definition")]
    public string RuleDefinition { get; set; } = null!;

    [Column("severity")]
    public string Severity { get; set; } = null!;

    [Column("applies_to")]
    public string AppliesTo { get; set; } = null!;

    [Column("is_active")]
    public bool IsActive { get; set; }

    [Column("created_at")]
    public DateTimeOffset CreatedAt { get; set; }

    [Column("updated_at")]
    public DateTimeOffset UpdatedAt { get; set; }

    private readonly List<Violation> _violations = new();
    public IEnumerable<Violation> Violations => _violations;
}

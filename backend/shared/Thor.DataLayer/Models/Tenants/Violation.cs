using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Thor.DataLayer.Models.Tenants;

/// <summary>
/// An active or historical breach of a <see cref="ControlRule"/> by an entity
/// (identified polymorphically by <see cref="EntityType"/>/<see cref="EntityId"/>).
/// </summary>
[Table("violation")]
public sealed class Violation
{
    [Key]
    [Column("id")]
    public Guid Id { get; set; }

    [Column("control_rule_id")]
    [ForeignKey(nameof(ControlRule))]
    public Guid ControlRuleId { get; set; }

    [Column("entity_type")]
    public string EntityType { get; set; } = null!;

    [Column("entity_id")]
    public Guid EntityId { get; set; }

    [Column("severity")]
    public string Severity { get; set; } = null!;

    [Column("detail")]
    public string Detail { get; set; } = null!;

    [Column("status")]
    public string Status { get; set; } = null!;

    [Column("is_active")]
    public bool IsActive { get; set; }

    [Column("created_at")]
    public DateTimeOffset CreatedAt { get; set; }

    [Column("updated_at")]
    public DateTimeOffset UpdatedAt { get; set; }

    public ControlRule ControlRule { get; set; } = null!;

    private readonly List<ViolationEvent> _events = new();
    public IEnumerable<ViolationEvent> Events => _events;
}

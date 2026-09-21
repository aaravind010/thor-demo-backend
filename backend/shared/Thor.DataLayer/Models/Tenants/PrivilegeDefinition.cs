using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Thor.DataLayer.Models.Tenants;

/// <summary>
/// A pattern (e.g. an access path or permission shape) used to evaluate whether an
/// account holds a privileged grant, via <see cref="PaeResult"/>.
/// </summary>
[Table("privilege_definition")]
public sealed class PrivilegeDefinition
{
    [Key]
    [Column("id")]
    public Guid Id { get; set; }

    [Column("name")]
    public string Name { get; set; } = null!;

    [Column("description")]
    public string Description { get; set; } = null!;

    [Column("pattern_type")]
    public string PatternType { get; set; } = null!;

    [Column("path_pattern")]
    public string PathPattern { get; set; } = null!;

    [Column("severity")]
    public string Severity { get; set; } = null!;

    [Column("is_active")]
    public bool IsActive { get; set; }

    [Column("created_at")]
    public DateTimeOffset CreatedAt { get; set; }

    [Column("updated_at")]
    public DateTimeOffset UpdatedAt { get; set; }

    public ICollection<PaiResult> PaeResults { get; set; } = new List<PaiResult>();
}

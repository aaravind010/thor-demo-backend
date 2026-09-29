using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Thor.DataLayer.Models.Tenants;

// Sandbox-only table for testing the tenant migration pipeline (expand / migrate / contract).
[Table("migration_probe")]
public sealed class MigrationProbe
{
    [Key]
    [Column("id")]
    public Guid Id { get; set; }

    [Column("new_note")]
    public string? NewNote { get; set; }

    [Column("score")]
    public int? Score { get; set; }
}

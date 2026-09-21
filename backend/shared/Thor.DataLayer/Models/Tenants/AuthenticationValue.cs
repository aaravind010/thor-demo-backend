using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Thor.DataLayer.Models.Tenants;

/// <summary>
/// The value of one authentication field (see <see cref="Models.AuthenticationField"/>
/// in the Master metadata DB) for one <see cref="AuthenticationMethod"/>. Lives in the
/// per-tenant database; <see cref="FieldId"/> is a cross-database reference with no
/// local FK. The secret itself lives in AWS Secrets Manager (see
/// docs/architecture/ADR-CONNECTOR-CREDENTIAL-MANAGEMENT.md) under one secret shared by
/// every <see cref="AuthenticationValue"/> for the same tenant + authentication type,
/// keyed by this row's <see cref="Id"/>; <see cref="SecretArn"/> is that secret's ARN.
/// </summary>
[Table("authentication_values")]
public sealed class AuthenticationValue
{
    [Key]
    [Column("id")]
    public Guid Id { get; set; }

    [Column("method_id")]
    [ForeignKey(nameof(AuthenticationMethod))]
    public Guid MethodId { get; set; }

    [Column("field_id")]
    public Guid FieldId { get; set; }

    [Column("secret_arn")]
    public string SecretArn { get; set; } = null!;

    [Column("created_at")]
    public DateTimeOffset CreatedAt { get; set; }

    [Column("updated_at")]
    public DateTimeOffset UpdatedAt { get; set; }

    [Column("created_by")]
    public string CreatedBy { get; set; } = null!;

    [Column("updated_by")]
    public string UpdatedBy { get; set; } = null!;

    public AuthenticationMethod AuthenticationMethod { get; set; } = null!;
}

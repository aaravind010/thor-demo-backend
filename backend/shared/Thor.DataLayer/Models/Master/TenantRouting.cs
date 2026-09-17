using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Thor.DataLayer.Models;

/// <summary>
/// Routing and infra metadata for a tenant in the Master metadata DB (see ADR §6.2) —
/// which Aurora cluster/database holds the tenant's data, its Secrets Manager credential
/// reference, and its Cognito user pool. One row per <see cref="Tenant"/>.
/// </summary>
[Table("tenant_routing", Schema = "auth")]
public sealed class TenantRouting
{
    // TenantId is both the primary key and the FK to Tenant — a shared-key one-to-one.
    [Key]
    [Column("tenant_id")]
    [ForeignKey(nameof(Tenant))]
    public Guid TenantId { get; set; }

    [Column("cluster_endpoint")]
    public string ClusterEndpoint { get; set; } = null!;

    [Column("database_name")]
    public string DatabaseName { get; set; } = null!;

    [Column("region")]
    public string Region { get; set; } = null!;

    [Column("user_pool_id")]
    public string UserPoolId { get; set; } = null!;

    [Column("app_client_Id")]
    public string AppClientId { get; set; } = null!;

    [Column("secret_arn")]
    public string SecretArn { get; set; } = null!;

    [Column("created_at")]
    public DateTimeOffset CreatedAt { get; set; }

    [Column("updated_at")]
    public DateTimeOffset UpdatedAt { get; set; }

    public Tenant Tenant { get; set; } = null!;
}

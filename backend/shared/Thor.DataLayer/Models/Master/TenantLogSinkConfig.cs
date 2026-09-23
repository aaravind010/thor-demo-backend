using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Thor.DataLayer.Models;

/// <summary>
/// The external HTTP log sink endpoint a tenant's log statements should be shipped to.
/// One row per <see cref="Tenant"/>; absence of a row means no external sink is configured
/// for that tenant (log statements are still captured by the service's other sinks, e.g. console).
/// </summary>
[Table("tenant_log_sink_config", Schema = "auth")]
public sealed class TenantLogSinkConfig
{
    [Key]
    [Column("tenant_id")]
    [ForeignKey(nameof(Tenant))]
    public Guid TenantId { get; set; }

    [Column("http_endpoint")]
    public string HttpEndpoint { get; set; } = null!;

    // Max events per HTTP request to this tenant's sink. Null means "use Serilog's own
    // default" (see ThorLoggingExtensions.DefaultBatchSizeLimit), not "unbounded".
    [Column("batch_size_limit")]
    public int? BatchSizeLimit { get; set; }

    public Tenant Tenant { get; set; } = null!;
}

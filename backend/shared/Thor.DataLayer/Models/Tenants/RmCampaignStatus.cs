using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Thor.DataLayer.Models.Tenants;

/// <summary>
/// A read-model row summarizing a <see cref="Campaign"/>'s item-decision counts as of
/// its <see cref="LastScan"/>.
/// </summary>
[Table("rm_campaign_status")]
public sealed class RmCampaignStatus
{
    [Key]
    [Column("campaign_id")]
    public Guid CampaignId { get; set; }

    [Column("name")]
    public string Name { get; set; } = null!;

    [Column("campaign_type")]
    public string CampaignType { get; set; } = null!;

    [Column("status")]
    public string Status { get; set; } = null!;

    [Column("due_date")]
    public DateTimeOffset? DueDate { get; set; }

    [Column("total_items")]
    public int TotalItems { get; set; }

    [Column("approved")]
    public int Approved { get; set; }

    [Column("pending")]
    public int Pending { get; set; }

    [Column("rejected")]
    public int Rejected { get; set; }

    [Column("escalated")]
    public int Escalated { get; set; }

    [Column("last_scan_id")]
    [ForeignKey(nameof(LastScan))]
    public Guid LastScanId { get; set; }

    [Column("last_refreshed_at")]
    public DateTimeOffset LastRefreshedAt { get; set; }

    public Scan LastScan { get; set; } = null!;
}

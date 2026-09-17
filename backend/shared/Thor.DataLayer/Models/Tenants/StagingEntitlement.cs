using System.ComponentModel.DataAnnotations.Schema;

namespace Thor.DataLayer.Models.Tenants;

/// <summary>
/// A staged entitlement/permission row (e.g. admin right, sudo grant) landed by a
/// connector ingestion run (see ADR §12 Ingestion &amp; CDC), prior to being hashed
/// and diffed against the canonical entitlement table to produce insert/update/delete
/// sets.
/// </summary>
[Table("staging_entitlement")]
public sealed class StagingEntitlement
{
    [Column("run_id")]
    public string RunId { get; set; } = null!;

    [Column("batch_seq")]
    public int BatchSeq { get; set; }
    
    [Column("source_id")]
    public Guid SourceId { get; set; }
    
    [Column("connector_type")]
    public short ConnectorType { get; set; }
    
    [Column("native_id")]
    public string NativeId { get; set; } = null!;

    [Column("entitlement_type")]
    public string EntitlementType { get; set; } = null!;

    [Column("name")]
    public string Name { get; set; } = null!;

    [Column("description")]
    public string Description { get; set; } = null!;

    [Column("is_admin")]
    public bool IsAdmin { get; set; }
    
    [Column("scope")]
    public string Scope { get; set; } = null!;

    [Column("instance_name")]
    public string InstanceName { get; set; } = null!;

    [Column("sudo_path")]
    public string SudoPath { get; set; } = null!;

    [Column("sudo_host")]
    public string SudoHost { get; set; } = null!;

    [Column("raw_attributes")]
    public string RawAttributes { get; set; } = null!;

    [Column("content_hash")]
    public string ContentHash { get; set; } = null!;

    [Column("received_at")]
    public DateTimeOffset ReceivedAt { get; set; }
}

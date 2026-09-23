using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace Thor.DataLayer.Models;

/// <summary>
/// A connector implementation (e.g. Active Directory, Microsoft 365) that
/// <see cref="AuthenticationType"/> and <see cref="ConnectorConfigField"/> rows are scoped to.
/// Lives in the Master metadata DB. Ids are fixed/manually assigned (not identity) so they
/// stay stable references from tenant-DB rows; do not renumber existing rows. See the
/// <c>[Comment]</c> attributes below (surfaced on the real table/column via
/// <c>COMMENT ON TABLE/COLUMN</c>) and ADR §6.2 for the cross-database reference convention
/// every tenant-DB table with a <c>connector_type</c> column follows.
/// </summary>
[Table("connector_types")]
[Comment("Canonical connector-type lookup (id -> name), see ADR §6.2. Every tenant-DB table "
    + "with a connector_type column (Source, Account, Grp, Asset, Entitlement, their Staging "
    + "counterparts, and ScanConnectorConfigValue) references this table's id at the "
    + "application level only — no physical FK, since those tables live in a separate "
    + "per-tenant database. Within the Master DB itself, AuthenticationType and "
    + "ConnectorConfigField reference it with a real FK.")]
public sealed class ConnectorType
{
    [Key]
    [Column("id")]
    [DatabaseGenerated(DatabaseGeneratedOption.None)]
    [Comment("Fixed, manually-assigned id — not identity. Do not renumber existing rows; "
        + "tenant-DB data references these ids with no physical FK to enforce it.")]
    public short Id { get; set; }

    [Column("name")]
    public string Name { get; set; } = null!;

    private readonly List<AuthenticationType> _authenticationTypes = new();
    public IEnumerable<AuthenticationType> AuthenticationTypes => _authenticationTypes;

    private readonly List<ConnectorConfigField> _connectorConfigFields = new();
    public IEnumerable<ConnectorConfigField> ConnectorConfigFields => _connectorConfigFields;
}

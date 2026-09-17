namespace Thor.Graph;

/// <summary>
/// Connection config for the shared Neptune cluster. Per ADR §6.1, Neptune is one regional,
/// multi-AZ, tenant-<em>partitioned</em> cluster — not database-per-tenant like Postgres — so
/// this is a single static config, not per-tenant routing through Thor.DataConnectionManager.
/// No credential/IAM fields today: auth is network-only (VPC + security groups), per current
/// decision; a future IAM/SigV4 mode would extend this record.
/// </summary>
public sealed record NeptuneOptions
{
    public required string Endpoint { get; init; }

    public int Port { get; init; } = 8182;

    public bool EnableSsl { get; init; } = true;
}

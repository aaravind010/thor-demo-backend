namespace Thor.Graph;

/// <summary>
/// Connection config for the shared Neptune cluster. Per ADR §6.1, Neptune is one regional,
/// multi-AZ, tenant-<em>partitioned</em> cluster — not database-per-tenant like Postgres — so
/// this is a single static config, not per-tenant routing through Thor.DataConnectionManager.
/// Every Gremlin and bulk-loader call is authenticated with IAM/SigV4 (see
/// <see cref="NeptuneSigV4Signer"/>); <see cref="Region"/> is the signing region.
/// </summary>
public sealed record NeptuneOptions
{
    public required string Endpoint { get; init; }

    public int Port { get; init; } = 8182;

    public bool EnableSsl { get; init; } = true;

    public required string Region { get; init; }
}

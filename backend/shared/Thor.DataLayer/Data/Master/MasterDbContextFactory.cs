using System.Collections.Concurrent;
using System.Security.Cryptography.X509Certificates;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Thor.DataLayer.Auth;

namespace Thor.DataLayer.Data;

public sealed class MasterDbContextFactory(IRdsIamTokenProvider tokenProvider) : IMasterDbContextFactory, IDisposable
{
    // The IAM token is valid ~15 min. Refresh it comfortably ahead of expiry; retry quickly on
    // a failed mint. Mirrors TenantConnectionManager: one data source (one pool) per Master
    // connection, with the password swapped out-of-band, so connections are reused instead of
    // paying a fresh TLS + IAM handshake per context.
    private static readonly TimeSpan TokenRefreshInterval = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan TokenRefreshFailureInterval = TimeSpan.FromSeconds(5);

    // Keyed by the (value-equal) connection info. In practice there is exactly one Master DB,
    // so this holds a single entry. Lazy so a race never builds (and leaks) a second pool.
    private readonly ConcurrentDictionary<MasterConnectionInfo, Lazy<NpgsqlDataSource>> _dataSources = new();

    public MasterDbContext Create(MasterConnectionInfo connectionInfo)
    {
        var dataSource = _dataSources
            .GetOrAdd(connectionInfo, info => new Lazy<NpgsqlDataSource>(() => BuildDataSource(info)))
            .Value;

        var options = new DbContextOptionsBuilder<MasterDbContext>()
            .UseNpgsql(dataSource)
            .Options;

        return new MasterDbContext(options);
    }

    // The password is supplied by a periodic provider (a fresh IAM token, generated locally),
    // never embedded in the connection string — so the pool key is stable across token rotations.
    // There is no password auth path.
    private NpgsqlDataSource BuildDataSource(MasterConnectionInfo connectionInfo)
    {
        var connectionString = new NpgsqlConnectionStringBuilder
        {
            Host = connectionInfo.Host,
            Port = connectionInfo.Port,
            Database = connectionInfo.Database,
            Username = connectionInfo.Username,
            SslMode = connectionInfo.UseSsl ? SslMode.Require : SslMode.Disable,
            // RDS Proxy has no GSS encryption. Npgsql's default (Prefer) first tries to acquire
            // Kerberos credentials natively, which can stall the open until Timeout elapses.
            GssEncryptionMode = GssEncryptionMode.Disable,
        }.ConnectionString;

        var builder = new NpgsqlDataSourceBuilder(connectionString);
        // The VPC has no internet egress. Without this, .NET on Linux tries to download missing
        // issuer certs of the RDS Proxy's ACM certificate (AIA) while building the chain, which
        // hangs the TLS handshake until Timeout — even though SslMode.Require accepts any cert.
        builder.UseSslClientAuthenticationOptionsCallback(options =>
            options.CertificateChainPolicy = new X509ChainPolicy
            {
                DisableCertificateDownloads = true,
                RevocationMode = X509RevocationMode.NoCheck,
            });
        builder.UsePeriodicPasswordProvider(
            (_, _) => new ValueTask<string>(
                tokenProvider.GenerateToken(
                    connectionInfo.Host, connectionInfo.Port, connectionInfo.Username, connectionInfo.Region)),
            TokenRefreshInterval,
            TokenRefreshFailureInterval);

        return builder.Build();
    }

    public void Dispose()
    {
        foreach (var dataSource in _dataSources.Values)
        {
            if (dataSource.IsValueCreated)
            {
                dataSource.Value.Dispose();
            }
        }
    }
}

using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Thor.DataLayer.Data;

public sealed class TenantDbContextFactory : ITenantDbContextFactory
{
    public TenantDbContext Create(TenantConnectionInfo connectionInfo)
    {
        var connectionStringBuilder = new NpgsqlConnectionStringBuilder
        {
            Host = connectionInfo.Host,
            Port = connectionInfo.Port,
            Database = connectionInfo.Database,
            Username = connectionInfo.Username,
            Password = connectionInfo.Password,
            SslMode = connectionInfo.UseSsl ? SslMode.Require : SslMode.Disable,
        };

        var options = new DbContextOptionsBuilder<TenantDbContext>()
            .UseNpgsql(connectionStringBuilder.ConnectionString)
            .Options;

        return new TenantDbContext(options);
    }

    public TenantDbContext Create(DbConnection connection, bool contextOwnsConnection = true)
    {
        var options = new DbContextOptionsBuilder<TenantDbContext>()
            .UseNpgsql(connection, contextOwnsConnection)
            .Options;

        return new TenantDbContext(options);
    }
}

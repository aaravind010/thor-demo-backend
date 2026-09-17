using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Thor.DataLayer.Data;

public sealed class MasterDbContextFactory : IMasterDbContextFactory
{
    public MasterDbContext Create(MasterConnectionInfo connectionInfo)
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

        var options = new DbContextOptionsBuilder<MasterDbContext>()
            .UseNpgsql(connectionStringBuilder.ConnectionString)
            .Options;

        return new MasterDbContext(options);
    }
}

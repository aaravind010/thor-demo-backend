using Microsoft.EntityFrameworkCore;
using Npgsql;
using Thor.DataLayer.Auth;

namespace Thor.DataLayer.Data;

public sealed class MasterDbContextFactory(IRdsIamTokenProvider tokenProvider) : IMasterDbContextFactory
{
    public MasterDbContext Create(MasterConnectionInfo connectionInfo)
    {
        // The password is a short-lived RDS IAM token minted per call (it expires ~15 min, so
        // it must not be cached). There is no password auth path.
        var token = tokenProvider.GenerateToken(
            connectionInfo.Host, connectionInfo.Port, connectionInfo.Username, connectionInfo.Region);

        var connectionStringBuilder = new NpgsqlConnectionStringBuilder
        {
            Host = connectionInfo.Host,
            Port = connectionInfo.Port,
            Database = connectionInfo.Database,
            Username = connectionInfo.Username,
            Password = token,
            SslMode = connectionInfo.UseSsl ? SslMode.Require : SslMode.Disable,
        };

        var options = new DbContextOptionsBuilder<MasterDbContext>()
            .UseNpgsql(connectionStringBuilder.ConnectionString)
            .Options;

        return new MasterDbContext(options);
    }
}

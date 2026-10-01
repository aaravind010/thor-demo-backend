using System.Data.Common;
using Microsoft.EntityFrameworkCore;

namespace Thor.DataLayer.Data;

public sealed class TenantDbContextFactory : ITenantDbContextFactory
{
    public TenantDbContext Create(DbConnection connection, bool contextOwnsConnection = true)
    {
        var options = new DbContextOptionsBuilder<TenantDbContext>()
            .UseNpgsql(connection, contextOwnsConnection)
            .Options;

        return new TenantDbContext(options);
    }
}

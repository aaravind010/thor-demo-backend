using Microsoft.EntityFrameworkCore;
using Thor.DataLayer.Data;

namespace Thor.Api.Test.TestFixtures;

/// <summary>
/// Backs <see cref="Thor.Api.Services.ConnectorAuthService"/> with a named EF Core InMemory database
/// instead of Postgres. Every context created for the same instance shares that database, so
/// data seeded (or written) through one context is visible to the next, matching how the real
/// <c>MasterDbContextFactory</c> hands out per-call contexts against the same physical DB.
/// </summary>
public sealed class InMemoryMasterDbContextFactory(string databaseName) : IMasterDbContextFactory
{
    public MasterDbContext Create(MasterConnectionInfo connectionInfo) =>
        new(new DbContextOptionsBuilder<MasterDbContext>()
            .UseInMemoryDatabase(databaseName)
            .Options);
}

namespace Thor.DataLayer.Data;

/// <summary>
/// Creates a <see cref="MasterDbContext"/> bound to the Master metadata DB, given
/// already-resolved connection parameters.
/// </summary>
public interface IMasterDbContextFactory
{
    MasterDbContext Create(MasterConnectionInfo connectionInfo);
}

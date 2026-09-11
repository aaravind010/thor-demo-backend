namespace Thor.DataConnectionManager.Exceptions;

/// <summary>
/// No routing row exists for the given tenant in the Master metadata DB.
/// </summary>
public sealed class TenantNotFoundException(Guid tenantId)
    : Exception($"No tenant routing found for tenant '{tenantId}'.")
{
    public Guid TenantId { get; } = tenantId;
}

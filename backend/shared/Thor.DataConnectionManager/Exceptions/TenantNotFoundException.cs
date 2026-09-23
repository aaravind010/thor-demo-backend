namespace Thor.DataConnectionManager.Exceptions;

/// <summary>
/// No routing row exists for the given tenant in the Master metadata DB.
/// </summary>
public sealed class TenantNotFoundException : Exception
{
    public Guid? TenantId { get; }

    public string? Subdomain { get; }

    public TenantNotFoundException(Guid tenantId)
        : base($"No tenant routing found for tenant '{tenantId}'.")
    {
        TenantId = tenantId;
    }

    public TenantNotFoundException(string subdomain)
        : base($"No tenant routing found for subdomain '{subdomain}'.")
    {
        Subdomain = subdomain;
    }
}

namespace Thor.Api.Exceptions;

/// <summary>No ScanConfig with the given id exists in the tenant database.</summary>
public sealed class ScanConfigNotFoundException(Guid scanConfigId) : Exception($"Scan configuration '{scanConfigId}' was not found.")
{
    public Guid ScanConfigId { get; } = scanConfigId;
}

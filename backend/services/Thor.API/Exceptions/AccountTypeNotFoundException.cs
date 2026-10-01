namespace Thor.Api.Exceptions;

/// <summary>The requested AccountType id doesn't exist in the tenant database.</summary>
public sealed class AccountTypeNotFoundException(Guid accountTypeId)
    : Exception($"Account type not found: {accountTypeId}.")
{
    public Guid AccountTypeId { get; } = accountTypeId;
}

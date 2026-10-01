namespace Thor.Api.Models;

public sealed record TenantIdentityProviderSummaryResponse(
    string ProviderName, string ProviderType, DateTime? CreatedAt, DateTime? UpdatedAt);

namespace Thor.Authorizer.Core.Auth.ApiKey;

public interface IApiKeyValidator
{
    Task<ApiKeyValidationResult> ValidateAsync(string keyMaterial);
}

namespace Thor.Authorizer.Core.Auth.Jwt;

public interface IJwtValidator
{
    Task<JwtValidationResult> ValidateAsync(
        string token,
        string userPoolId,
        string appClientId,
        string region);
}

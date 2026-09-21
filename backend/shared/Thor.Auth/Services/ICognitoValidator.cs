namespace Thor.Auth;

public interface ICognitoValidator
{
    Task<JwtValidationResult> ValidateAsync(
        string token,
        string userPoolId,
        string appClientId,
        string region);
}

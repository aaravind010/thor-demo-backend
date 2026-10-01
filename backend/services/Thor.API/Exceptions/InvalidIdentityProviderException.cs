namespace Thor.Api.Exceptions;

/// <summary>The identity provider request is invalid — rejected by Thor's checks or by Cognito.</summary>
public sealed class InvalidIdentityProviderException(string message, Exception? innerException = null)
    : Exception(message, innerException);

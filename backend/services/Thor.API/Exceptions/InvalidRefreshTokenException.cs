namespace Thor.Api.Exceptions;

/// <summary>The presented refresh token is malformed, unknown, revoked, expired, or fails hash verification.</summary>
public sealed class InvalidRefreshTokenException() : Exception("Invalid or expired refresh token.");

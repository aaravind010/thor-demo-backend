namespace Thor.Api.Exceptions;

/// <summary>The presented x-task-api-key is malformed, unknown, revoked, expired, or fails hash verification.</summary>
public sealed class InvalidApiKeyException() : Exception("Invalid or expired API key.");

namespace Thor.Api.Models;

/// <summary>Request body for <c>POST /register/refresh</c>.</summary>
public sealed record RefreshRequest(string RefreshToken);

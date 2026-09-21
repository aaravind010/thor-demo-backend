namespace Thor.TaskApi.Models;

/// <summary>Request body for <c>POST /register/refresh</c>.</summary>
public sealed record RefreshRequest(string RefreshToken);

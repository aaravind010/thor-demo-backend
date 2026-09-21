namespace Thor.TaskApi.Models;

/// <summary>Response shared by <c>POST /register</c> and <c>POST /register/refresh</c>.</summary>
public sealed record TaskAuthResponse(string Token, string RefreshToken, string RoleType);

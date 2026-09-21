namespace Thor.TaskApi.Models;

/// <summary>Request body for <c>POST /register</c>. The API key itself travels in the x-task-api-key header.</summary>
public sealed record RegisterRequest(string RoleType);

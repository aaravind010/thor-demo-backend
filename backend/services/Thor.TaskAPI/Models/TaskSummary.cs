namespace Thor.TaskApi.Models;

/// <summary>Placeholder task DTO returned by the scaffold endpoint.</summary>
public record TaskSummary(Guid Id, string Name, string Status);

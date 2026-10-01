using System.Text.Json;

namespace Thor.TaskApi.Models;

/// <summary>
/// Request body for <c>POST /tasks/{taskId}/progress</c>. <see cref="Attributes"/> is an
/// arbitrary JSON object (e.g. <c>{"users": 20, "groups": 10}</c>) whose fields can be of any
/// value type and aren't known ahead of time, so it's kept as a raw <see cref="JsonElement"/>
/// rather than a typed model.
/// </summary>
public sealed record TaskProgressRequest(long TimeElapsed, int PercentageComplete, JsonElement Attributes);

namespace Thor.Core.Logging;

/// <summary>
/// A tenant's resolved external HTTP log sink configuration. <see cref="BatchSizeLimit"/> is
/// the max number of events sent per HTTP request (Serilog's own default is 1000 when null).
/// </summary>
public sealed record TenantLogSinkOptions(string Endpoint, int? BatchSizeLimit = null);

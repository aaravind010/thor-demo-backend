using Thor.DataLayer.Models.Tenants;

namespace Thor.Api.Models;

public sealed record CreateScanRequest(Guid ScanConfigId, string ScanType = ScanTriggerType.Instant);

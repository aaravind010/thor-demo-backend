using Thor.TenantProvisioning.Core.Models;

namespace Thor.TenantProvisioning.Test;

/// <summary>An input state for step tests; override via <c>with</c>.</summary>
internal static class TestState
{
    public static ProvisioningState Sample() => new()
    {
        DisplayName = "Acme Inc",
        Subdomain = "acme",
        AdminEmail = "admin@acme.example.com",
        TierId = 1,
        IsolationTypeId = 1,
    };
}

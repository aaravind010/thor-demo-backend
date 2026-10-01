using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Thor.Api.Constants;

namespace Thor.Api.Filters;

/// <summary>
/// Allows the request only when the caller is in the tenant admin group, read from the trusted
/// <see cref="TenantConstants.CallerGroupsHeaderName"/> header. Fails closed: a missing or empty
/// header (machine callers, unauthenticated routes) is a 403. An authorization filter so it runs
/// before model binding — a non-admin learns nothing about request validation.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class RequireTenantAdminAttribute : Attribute, IAuthorizationFilter
{
    public void OnAuthorization(AuthorizationFilterContext context)
    {
        var groupsHeader = context.HttpContext.Request.Headers[TenantConstants.CallerGroupsHeaderName].ToString();

        var isAdmin = groupsHeader
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Contains(TenantConstants.AdminGroup, StringComparer.Ordinal);

        if (!isAdmin)
        {
            context.Result = new StatusCodeResult(StatusCodes.Status403Forbidden);
        }
    }
}

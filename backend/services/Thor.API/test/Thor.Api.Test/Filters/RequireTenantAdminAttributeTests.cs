using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Thor.Api.Constants;
using Thor.Api.Controllers.V1;
using Thor.Api.Filters;

namespace Thor.Api.Test.Filters;

public class RequireTenantAdminAttributeTests
{
    private static AuthorizationFilterContext Run(string? groupsHeader)
    {
        var httpContext = new DefaultHttpContext();
        if (groupsHeader is not null)
        {
            httpContext.Request.Headers[TenantConstants.CallerGroupsHeaderName] = groupsHeader;
        }

        var context = new AuthorizationFilterContext(
            new ActionContext(httpContext, new RouteData(), new ActionDescriptor()), []);

        new RequireTenantAdminAttribute().OnAuthorization(context);
        return context;
    }

    [Theory]
    [InlineData(null)] // header absent
    [InlineData("")] // machine callers get an empty value
    [InlineData("auditors")]
    [InlineData("xadmins")] // substring of another group must not match
    [InlineData("Admins")] // group names are case-sensitive
    [InlineData("admins-readonly,auditors")]
    public void OnAuthorization_NonAdmin_Returns403(string? groupsHeader)
    {
        var context = Run(groupsHeader);

        context.Result.Should().BeOfType<StatusCodeResult>()
            .Which.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
    }

    [Theory]
    [InlineData("admins")]
    [InlineData("auditors,admins")]
    [InlineData("auditors, admins")]
    public void OnAuthorization_Admin_LetsRequestThrough(string groupsHeader)
    {
        var context = Run(groupsHeader);

        context.Result.Should().BeNull();
    }

    [Fact]
    public void IdentityProvidersController_IsAdminOnly()
    {
        typeof(IdentityProvidersController).GetCustomAttributes(typeof(RequireTenantAdminAttribute), inherit: true)
            .Should().ContainSingle();
    }
}

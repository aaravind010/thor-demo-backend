using System.Reflection;
using Asp.Versioning;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Thor.Api.Controllers.V1;

namespace Thor.Api.Test.Controllers.V1;

public class RegisterControllerVersioningTests
{
    [Fact]
    public void RegisterController_IsInV1Namespace()
    {
        typeof(RegisterController).Namespace.Should().Be("Thor.Api.Controllers.V1");
    }

    [Fact]
    public void RegisterController_DeclaresApiVersionOneZero()
    {
        var apiVersionAttribute = typeof(RegisterController).GetCustomAttribute<ApiVersionAttribute>();

        apiVersionAttribute.Should().NotBeNull();
        apiVersionAttribute!.Versions.Should().ContainSingle()
            .Which.Should().Be(new ApiVersion(1, 0));
    }

    [Fact]
    public void RegisterController_RouteIncludesVersionSegment()
    {
        var routeAttribute = typeof(RegisterController).GetCustomAttribute<RouteAttribute>();

        routeAttribute.Should().NotBeNull();
        routeAttribute!.Template.Should().Be("v{version:apiVersion}/register");
    }
}

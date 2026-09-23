using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using NSubstitute;
using Thor.Api.Constants;
using Thor.Api.Controllers.V1;
using Thor.Api.Models;
using Thor.Api.Services;
using Thor.DataConnectionManager;
using Thor.DataConnectionManager.Exceptions;
using Thor.DataLayer.Data;

namespace Thor.Api.Test.Controllers.V1;

public class AccountTypesControllerTests
{
    private readonly string _tenantDbName = $"tenant-{Guid.NewGuid()}";

    // The in-memory provider doesn't support real transactions; AccountTypeService wraps its
    // writes in one, so this warning is expected and safe to ignore for these tests.
    private TenantDbContext CreateTenantDbContext() =>
        new(new DbContextOptionsBuilder<TenantDbContext>()
            .UseInMemoryDatabase(_tenantDbName)
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options);

    private AccountTypesController CreateController(ITenantConnectionManager? tenantConnectionManager = null)
    {
        var manager = tenantConnectionManager ?? CreateDefaultTenantConnectionManager();
        var service = new AccountTypeService(manager);

        return new AccountTypesController(service)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() },
        };
    }

    private ITenantConnectionManager CreateDefaultTenantConnectionManager()
    {
        var manager = Substitute.For<ITenantConnectionManager>();
        manager.GetTenantDbContextAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult(CreateTenantDbContext()));
        return manager;
    }

    // Sets the trusted tenant header the controller reads directly (see the comment on
    // AccountTypesController.Create). Defaults to a fresh random guid string; pass a non-guid
    // string to exercise the "invalid header" path, or includeTenantHeader: false to omit it.
    private static void SetHeaders(
        AccountTypesController controller,
        bool includeTenantHeader = true,
        string? tenantHeaderValue = null)
    {
        if (includeTenantHeader)
        {
            controller.Request.Headers[TenantConstants.TenantHeaderName] = tenantHeaderValue ?? Guid.NewGuid().ToString();
        }
    }

    private static CreateAccountTypeRequest ValidRequest() =>
        new(Name: "Service Account", Description: "Non-human automated account", IsHuman: false);

    /// <summary>A request with no tenant header at all is rejected before the service is called.</summary>
    [Fact]
    public async Task Create_MissingTenantHeader_ReturnsBadRequest()
    {
        var controller = CreateController();
        SetHeaders(controller, includeTenantHeader: false);

        var result = await controller.Create(ValidRequest(), CancellationToken.None);

        result.Result.Should().BeOfType<BadRequestObjectResult>();
    }

    /// <summary>A tenant header that isn't a parseable guid is rejected before the service is called.</summary>
    [Fact]
    public async Task Create_InvalidTenantHeader_ReturnsBadRequest()
    {
        var controller = CreateController();
        SetHeaders(controller, tenantHeaderValue: "not-a-guid");

        var result = await controller.Create(ValidRequest(), CancellationToken.None);

        result.Result.Should().BeOfType<BadRequestObjectResult>();
    }

    /// <summary>A null, empty, or whitespace-only Name is rejected with the exact validation message.</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Create_MissingName_ReturnsBadRequest(string? name)
    {
        var controller = CreateController();
        SetHeaders(controller);
        var request = ValidRequest() with { Name = name! };

        var result = await controller.Create(request, CancellationToken.None);

        result.Result.Should().BeOfType<BadRequestObjectResult>()
            .Which.Value.Should().Be("Name is required.");
    }

    /// <summary>A null, empty, or whitespace-only Description is rejected with the exact validation message.</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Create_MissingDescription_ReturnsBadRequest(string? description)
    {
        var controller = CreateController();
        SetHeaders(controller);
        var request = ValidRequest() with { Description = description! };

        var result = await controller.Create(request, CancellationToken.None);

        result.Result.Should().BeOfType<BadRequestObjectResult>()
            .Which.Value.Should().Be("Description is required.");
    }

    /// <summary>
    /// A tenant header that parses but has no routing (ITenantConnectionManager fails closed with
    /// TenantNotFoundException) surfaces as 403, not a validation error.
    /// </summary>
    [Fact]
    public async Task Create_TenantNotFound_ReturnsForbidden()
    {
        var tenantId = Guid.NewGuid();
        var manager = Substitute.For<ITenantConnectionManager>();
        manager.GetTenantDbContextAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromException<TenantDbContext>(new TenantNotFoundException(tenantId)));
        var controller = CreateController(manager);
        SetHeaders(controller, tenantHeaderValue: tenantId.ToString());

        var result = await controller.Create(ValidRequest(), CancellationToken.None);

        result.Result.Should().BeOfType<StatusCodeResult>()
            .Which.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
    }

    /// <summary>A fully valid request persists an AccountType and returns 201 with the new row.</summary>
    [Fact]
    public async Task Create_ValidRequest_PersistsAccountTypeAndReturnsCreated()
    {
        var controller = CreateController();
        SetHeaders(controller);

        var result = await controller.Create(ValidRequest(), CancellationToken.None);

        var objectResult = result.Result.Should().BeOfType<ObjectResult>().Subject;
        objectResult.StatusCode.Should().Be(StatusCodes.Status201Created);
        var response = objectResult.Value.Should().BeOfType<AccountTypeResponse>().Subject;
        response.Id.Should().NotBeEmpty();
        response.Name.Should().Be("Service Account");
        response.Description.Should().Be("Non-human automated account");
        response.IsHuman.Should().BeFalse();

        using var verifyDb = CreateTenantDbContext();
        var persisted = verifyDb.AccountTypes.Single(a => a.Id == response.Id);
        persisted.Name.Should().Be("Service Account");
        persisted.Description.Should().Be("Non-human automated account");
        persisted.IsHuman.Should().BeFalse();
    }
}

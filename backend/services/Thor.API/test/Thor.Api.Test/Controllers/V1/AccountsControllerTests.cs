using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Thor.Api.Controllers.V1;
using Thor.Api.Models;
using Thor.Api.Services;
using Thor.Api.Test.Support;
using Thor.DataConnectionManager;

namespace Thor.Api.Test.Controllers.V1;

public class AccountsControllerTests
{
    private readonly InMemoryTenant _tenant = new();
    private readonly Guid _sourceId = Guid.NewGuid();
    private readonly Guid _accountTypeId = Guid.NewGuid();

    public AccountsControllerTests() =>
        _tenant.Seed(TestEntities.Source(_sourceId), TestEntities.AccountType(_accountTypeId));

    private AccountsController CreateController(
        ITenantConnectionManager? manager = null, bool includeTenantHeader = true, string? tenantHeaderValue = null) =>
        InMemoryTenant.WithTenantHeader(
            new AccountsController(new AccountService(manager ?? _tenant.ConnectionManager())),
            includeTenantHeader, tenantHeaderValue);

    private static CursorPage<AccountResponse> Page(ActionResult<CursorPage<AccountResponse>> result) =>
        result.Result.Should().BeOfType<OkObjectResult>().Which.Value.Should().BeOfType<CursorPage<AccountResponse>>().Subject;

    [Fact]
    public async Task List_MissingTenantHeader_ReturnsBadRequest()
    {
        var result = await CreateController(includeTenantHeader: false).List(null, null, null, null);

        result.Result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task List_TenantNotFound_ReturnsForbidden()
    {
        var result = await CreateController(InMemoryTenant.UnknownTenantConnectionManager()).List(null, null, null, null);

        result.Result.Should().BeOfType<StatusCodeResult>().Which.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(501)]
    public async Task List_LimitOutOfRange_ReturnsBadRequest(int limit)
    {
        var result = await CreateController().List(null, null, null, null, limit);

        result.Result.Should().BeOfType<BadRequestObjectResult>().Which.Value.Should().Be("limit must be between 1 and 500.");
    }

    /// <summary>
    /// Walking pages with nextCursor visits every row exactly once in id order, and the last page
    /// reports no further cursor.
    /// </summary>
    [Fact]
    public async Task List_CursorPaging_VisitsEveryRowOnceThenStops()
    {
        var accounts = Enumerable.Range(0, 5).Select(_ => TestEntities.Account(_sourceId, _accountTypeId)).ToArray();
        _tenant.Seed(accounts);

        var first = Page(await CreateController().List(null, null, null, after: null, limit: 2));
        var second = Page(await CreateController().List(null, null, null, after: first.NextCursor, limit: 2));
        var third = Page(await CreateController().List(null, null, null, after: second.NextCursor, limit: 2));

        first.Items.Should().HaveCount(2);
        first.NextCursor.Should().Be(first.Items[^1].Id);
        second.Items.Should().HaveCount(2);
        third.Items.Should().HaveCount(1);
        third.NextCursor.Should().BeNull();

        var visited = first.Items.Concat(second.Items).Concat(third.Items).Select(a => a.Id).ToList();
        visited.Should().Equal(accounts.Select(a => a.Id).Order());
    }

    /// <summary>When the rows exactly fill a page there is no phantom next page.</summary>
    [Fact]
    public async Task List_RowsExactlyFillPage_HasNoNextCursor()
    {
        _tenant.Seed(TestEntities.Account(_sourceId, _accountTypeId), TestEntities.Account(_sourceId, _accountTypeId));

        var page = Page(await CreateController().List(null, null, null, after: null, limit: 2));

        page.Items.Should().HaveCount(2);
        page.NextCursor.Should().BeNull();
    }

    [Fact]
    public async Task List_FiltersBySourceAndDeletedState()
    {
        var otherSourceId = Guid.NewGuid();
        var live = TestEntities.Account(_sourceId, _accountTypeId);
        _tenant.Seed(
            TestEntities.Source(otherSourceId),
            live,
            TestEntities.Account(_sourceId, _accountTypeId, isDeleted: true),
            TestEntities.Account(otherSourceId, _accountTypeId));

        var page = Page(await CreateController().List(sourceId: _sourceId, accountTypeId: null, isDeleted: false, after: null));

        page.Items.Select(a => a.Id).Should().Equal(live.Id);
    }

    /// <summary>raw_attributes is left out of list pages and only returned by the single-row read.</summary>
    [Fact]
    public async Task RawAttributes_OnlyReturnedByGet()
    {
        var account = TestEntities.Account(_sourceId, _accountTypeId);
        _tenant.Seed(account);

        var page = Page(await CreateController().List(null, null, null, null));
        var single = await CreateController().Get(account.Id, CancellationToken.None);

        page.Items.Single().RawAttributes.Should().BeNull();
        single.Result.Should().BeOfType<OkObjectResult>().Which.Value.Should().BeOfType<AccountResponse>()
            .Which.RawAttributes.Should().Be(account.RawAttributes);
    }

    [Fact]
    public async Task Get_Missing_ReturnsNotFound()
    {
        var result = await CreateController().Get(Guid.NewGuid(), CancellationToken.None);

        result.Result.Should().BeOfType<NotFoundResult>();
    }

    [Fact]
    public async Task Get_InvalidTenantHeader_ReturnsBadRequest()
    {
        var result = await CreateController(tenantHeaderValue: "not-a-guid").Get(Guid.NewGuid(), CancellationToken.None);

        result.Result.Should().BeOfType<BadRequestObjectResult>();
    }
}

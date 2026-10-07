using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Thor.DataConnectionManager;
using Thor.DataLayer.Data;
using Thor.DataLayer.Models.Tenants;
using Thor.TenantProvisioning.Core.Steps;

namespace Thor.TenantProvisioning.Test.Steps;

public sealed class SeedUnclassifiedAccountTypeStepTests
{
    private readonly ITenantConnectionManager _connections = Substitute.For<ITenantConnectionManager>();
    private readonly DbContextOptions<TenantDbContext> _dbOptions =
        new DbContextOptionsBuilder<TenantDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;

    public SeedUnclassifiedAccountTypeStepTests() =>
        _connections.GetTenantDbContextAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(_ => new TenantDbContext(_dbOptions));

    private SeedUnclassifiedAccountTypeStep CreateStep() =>
        new(_connections, NullLogger<SeedUnclassifiedAccountTypeStep>.Instance);

    private async Task<List<AccountType>> AccountTypesAsync()
    {
        using var db = new TenantDbContext(_dbOptions);
        return await db.AccountTypes.ToListAsync();
    }

    [Fact]
    public async Task Throws_when_tenant_id_missing()
    {
        var act = () => CreateStep().RunAsync(TestState.Sample() with { TenantId = null });
        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task Seeds_unclassified_through_the_connection_manager_for_the_tenant()
    {
        var tenantId = Guid.NewGuid();

        await CreateStep().RunAsync(TestState.Sample() with { TenantId = tenantId });

        await _connections.Received(1).GetTenantDbContextAsync(tenantId, Arg.Any<CancellationToken>());
        var seeded = (await AccountTypesAsync()).Should().ContainSingle().Subject;
        seeded.Id.Should().Be(WellKnownAccountTypes.Unclassified);
        seeded.Name.Should().Be(WellKnownAccountTypes.UnclassifiedName);
        seeded.Description.Should().Be(WellKnownAccountTypes.UnclassifiedDescription);
        seeded.IsHuman.Should().BeFalse();
    }

    [Fact]
    public async Task Is_idempotent_when_run_twice()
    {
        var state = TestState.Sample() with { TenantId = Guid.NewGuid() };

        await CreateStep().RunAsync(state);
        var act = () => CreateStep().RunAsync(state);

        await act.Should().NotThrowAsync();
        (await AccountTypesAsync()).Should().ContainSingle();
    }

    [Fact]
    public async Task Leaves_an_existing_unclassified_row_untouched()
    {
        using (var db = new TenantDbContext(_dbOptions))
        {
            db.AccountTypes.Add(new AccountType
            {
                Id = WellKnownAccountTypes.Unclassified,
                Name = "Unclassified",
                Description = "Edited by an operator",
                IsHuman = false,
            });
            await db.SaveChangesAsync();
        }

        await CreateStep().RunAsync(TestState.Sample() with { TenantId = Guid.NewGuid() });

        var rows = await AccountTypesAsync();
        rows.Should().ContainSingle().Which.Description.Should().Be("Edited by an operator");
    }

    [Fact]
    public async Task Returns_the_state_unchanged()
    {
        var state = TestState.Sample() with { TenantId = Guid.NewGuid() };

        var result = await CreateStep().RunAsync(state);

        result.Should().Be(state);
    }
}

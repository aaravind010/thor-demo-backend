using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Thor.TenantProvisioning.Core.Abstractions;
using Thor.TenantProvisioning.Core.Steps;

namespace Thor.TenantProvisioning.Test.Steps;

public sealed class CreateTenantTablesStepTests
{
    private readonly ITenantSchemaMigrator _migrator = Substitute.For<ITenantSchemaMigrator>();

    private CreateTenantTablesStep CreateStep() =>
        new(_migrator, NullLogger<CreateTenantTablesStep>.Instance);

    [Fact]
    public async Task Throws_when_database_name_missing()
    {
        var act = () => CreateStep().RunAsync(TestState.Sample() with { RwDbUser = "tenant_acme_rw", RoDbUser = "tenant_acme_ro" });
        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task Throws_when_db_users_missing()
    {
        var act = () => CreateStep().RunAsync(TestState.Sample() with { DatabaseName = "tenant_acme" });
        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task Migrates_schema_once_database_and_roles_are_set()
    {
        var state = TestState.Sample() with
        {
            DatabaseName = "tenant_acme",
            RwDbUser = "tenant_acme_rw",
            RoDbUser = "tenant_acme_ro",
        };

        var result = await CreateStep().RunAsync(state);

        result.Should().Be(state);
        await _migrator.Received(1).MigrateAsync(
            Arg.Is<TenantSchemaMigrationRequest>(r =>
                r != null && r.DatabaseName == "tenant_acme" && r.RwDbUser == "tenant_acme_rw" && r.RoDbUser == "tenant_acme_ro"),
            Arg.Any<CancellationToken>());
    }
}

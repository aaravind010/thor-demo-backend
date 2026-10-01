using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using NSubstitute;
using Thor.DataConnectionManager;
using Thor.DataLayer.Data;
using Thor.DataLayer.Models;
using Thor.DataLayer.Models.Tenants;
using Thor.TaskApi.Services;

namespace Thor.TaskApi.Test.Services;

public class TaskServiceTests
{
    private const short ConnectorTypeId = 1;

    private readonly string _tenantDbName = $"tenant-{Guid.NewGuid()}";
    private readonly string _masterDbName = $"master-{Guid.NewGuid()}";

    private TenantDbContext CreateTenantDbContext() =>
        new(new DbContextOptionsBuilder<TenantDbContext>()
            .UseInMemoryDatabase(_tenantDbName)
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options);

    private MasterDbContext CreateMasterDbContext() =>
        new(new DbContextOptionsBuilder<MasterDbContext>().UseInMemoryDatabase(_masterDbName).Options);

    private void SeedTenantDb(Action<TenantDbContext> seed)
    {
        using var context = CreateTenantDbContext();
        seed(context);
        context.SaveChanges();
    }

    private void SeedMasterDb(Action<MasterDbContext> seed)
    {
        using var context = CreateMasterDbContext();
        seed(context);
        context.SaveChanges();
    }

    private TaskService CreateService(IAuthenticationSecretReader? secretReader = null)
    {
        var tenantConnectionManager = Substitute.For<ITenantConnectionManager>();
        tenantConnectionManager.GetTenantDbContextAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult(CreateTenantDbContext()));

        var masterFactory = Substitute.For<IMasterDbContextFactory>();
        masterFactory.Create(Arg.Any<MasterConnectionInfo>()).Returns(_ => CreateMasterDbContext());

        var masterConnectionInfo = new MasterConnectionInfo("host", "db", "user", "password");

        return new TaskService(
            tenantConnectionManager,
            new ScanTaskReclaimOptions(TimeSpan.FromMinutes(30), MaxRetries: 10),
            masterFactory,
            masterConnectionInfo,
            secretReader ?? Substitute.For<IAuthenticationSecretReader>());
    }

    /// <summary>
    /// Seeds a full task -> scan -> scan config chain, with one connector config field/value and
    /// two authentication field/values that share one SecretArn (as every AuthenticationValue for
    /// one AuthenticationMethod does — see docs/architecture/ADR-CONNECTOR-CREDENTIAL-MANAGEMENT.md),
    /// backed by a secret reader that resolves both AuthenticationValue ids from that one secret.
    /// </summary>
    private (Guid TaskId, Guid ScanId, Guid ScanConfigId, IAuthenticationSecretReader SecretReader) SeedFullTask()
    {
        var connectorConfigFieldId = Guid.NewGuid();
        var authTypeId = Guid.NewGuid();
        var authFieldId = Guid.NewGuid();
        var authValueId = Guid.NewGuid();
        var secondAuthFieldId = Guid.NewGuid();
        var secondAuthValueId = Guid.NewGuid();
        const string secretArn = "arn:aws:secretsmanager:local:000000000000:secret:tenant/x";

        SeedMasterDb(db =>
        {
            db.ConnectorConfigFields.Add(new ConnectorConfigField
            {
                Id = connectorConfigFieldId,
                ConnectorTypeId = ConnectorTypeId,
                FieldName = "base_url",
                DisplayName = "Base URL",
                InputType = "text",
                Required = false,
            });
            db.AuthenticationFields.Add(new AuthenticationField
            {
                Id = authFieldId,
                TypeId = authTypeId,
                Name = "api_key",
                DisplayName = "API Key",
                InputType = "password",
            });
            db.AuthenticationFields.Add(new AuthenticationField
            {
                Id = secondAuthFieldId,
                TypeId = authTypeId,
                Name = "client_secret",
                DisplayName = "Client Secret",
                InputType = "password",
            });
        });

        var sourceId = Guid.NewGuid();
        var authMethodId = Guid.NewGuid();
        var scanConfigId = Guid.NewGuid();
        var scanId = Guid.NewGuid();
        var taskId = Guid.NewGuid();

        SeedTenantDb(db =>
        {
            db.Sources.Add(new Source
            {
                Id = sourceId,
                ConnectorType = ConnectorTypeId,
                Name = "source-1",
                Config = "{}",
                IsActive = true,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow,
            });

            var authMethod = new AuthenticationMethod
            {
                Id = authMethodId,
                TypeId = authTypeId,
                Name = "auth-method-1",
            };
            authMethod.AuthenticationValues.Add(new AuthenticationValue
            {
                Id = authValueId,
                MethodId = authMethodId,
                FieldId = authFieldId,
                SecretArn = secretArn,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow,
                CreatedBy = "actor-1",
                UpdatedBy = "actor-1",
            });
            authMethod.AuthenticationValues.Add(new AuthenticationValue
            {
                Id = secondAuthValueId,
                MethodId = authMethodId,
                FieldId = secondAuthFieldId,
                SecretArn = secretArn,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow,
                CreatedBy = "actor-1",
                UpdatedBy = "actor-1",
            });
            db.AuthenticationMethods.Add(authMethod);

            var scanConfig = new ScanConfig
            {
                Id = scanConfigId,
                Name = "scan-config-1",
                AuthMethodId = authMethodId,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow,
                CreatedBy = "actor-1",
                UpdatedBy = "actor-1",
            };
            scanConfig.ConnectorConfigValues.Add(new ScanConnectorConfigValue
            {
                Id = Guid.NewGuid(),
                ScanConfigId = scanConfigId,
                ConnectorType = ConnectorTypeId,
                ConfigId = connectorConfigFieldId,
                Value = "https://example.com",
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow,
                CreatedBy = "actor-1",
                UpdatedBy = "actor-1",
            });
            db.ScanConfigs.Add(scanConfig);

            db.Scans.Add(new Scan
            {
                Id = scanId,
                ScanConfigId = scanConfigId,
                ScanType = ScanTriggerType.Instant,
                Status = ScanStatus.InProgress,
                TotalTasks = 1,
                CompletedTasks = 0,
            });

            db.Tasks.Add(new ScanTask
            {
                Id = taskId,
                ScanId = scanId,
                SourceId = sourceId,
                Status = ScanTaskStatus.InProgress,
                CreatedAt = DateTimeOffset.UtcNow,
            });
        });

        var secretReader = Substitute.For<IAuthenticationSecretReader>();
        secretReader.GetValuesAsync(secretArn, Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult<IReadOnlyDictionary<Guid, string>>(
                new Dictionary<Guid, string> { [authValueId] = "s3cr3t-value", [secondAuthValueId] = "s3cr3t-value-2" }));

        return (taskId, scanId, scanConfigId, secretReader);
    }

    [Fact]
    public async Task GetSettingsAsync_UnknownTaskId_ReturnsNull()
    {
        var service = CreateService();

        var result = await service.GetSettingsAsync(Guid.NewGuid(), Guid.NewGuid(), CancellationToken.None);

        result.Should().BeNull();
    }

    [Fact]
    public async Task GetSettingsAsync_KnownTask_ReturnsIdentifiersAndFieldNameKeyedJson()
    {
        var (taskId, scanId, scanConfigId, secretReader) = SeedFullTask();
        var service = CreateService(secretReader);

        var result = await service.GetSettingsAsync(Guid.NewGuid(), taskId, CancellationToken.None);

        result.Should().NotBeNull();
        result!.TaskId.Should().Be(taskId);
        result.ScanId.Should().Be(scanId);
        result.ScanConfigId.Should().Be(scanConfigId);

        var settings = JsonSerializer.Deserialize<Dictionary<string, string>>(result.Settings);
        settings.Should().ContainKey("base_url").WhoseValue.Should().Be("https://example.com");

        var authMethod = JsonSerializer.Deserialize<Dictionary<string, string>>(result.AuthMethod);
        authMethod.Should().ContainKey("api_key").WhoseValue.Should().Be("s3cr3t-value");
        authMethod.Should().ContainKey("client_secret").WhoseValue.Should().Be("s3cr3t-value-2");
    }

    /// <summary>
    /// Both AuthenticationValues seeded by <see cref="SeedFullTask"/> share one SecretArn (they
    /// belong to the same AuthenticationMethod, and every value for one method shares its
    /// tenant+type secret) — the secret reader must be called once for that ARN, not once per
    /// AuthenticationValue, since Secrets Manager calls are grouped by ARN specifically to keep
    /// their count down.
    /// </summary>
    [Fact]
    public async Task GetSettingsAsync_MultipleValuesShareOneSecretArn_ReadsThatSecretOnlyOnce()
    {
        var (taskId, _, _, secretReader) = SeedFullTask();
        var service = CreateService(secretReader);

        await service.GetSettingsAsync(Guid.NewGuid(), taskId, CancellationToken.None);

        await secretReader.Received(1).GetValuesAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AddProgressAsync_UnknownTaskId_ReturnsNull()
    {
        var service = CreateService();

        var result = await service.AddProgressAsync(
            Guid.NewGuid(), Guid.NewGuid(), timeElapsed: 3000, percentageComplete: 30, attributesJson: "{}", CancellationToken.None);

        result.Should().BeNull();
    }

    [Fact]
    public async Task AddProgressAsync_KnownTask_InsertsRowAndReturnsItsId()
    {
        var (taskId, _, _, _) = SeedFullTask();
        var service = CreateService();
        const string attributesJson = """{"users":20,"groups":10}""";

        var result = await service.AddProgressAsync(
            Guid.NewGuid(), taskId, timeElapsed: 3000, percentageComplete: 30, attributesJson, CancellationToken.None);

        result.Should().NotBeNull();

        using var tenantDb = CreateTenantDbContext();
        var progress = await tenantDb.TaskProgresses.SingleAsync(p => p.Id == result!.Value);
        progress.TaskId.Should().Be(taskId);
        progress.PercentageCompleted.Should().Be(30);
        progress.TimeElapsed.Should().Be(3000);
        progress.Attributes.Should().Be(attributesJson);
    }

    /// <summary>
    /// Every call inserts a new row rather than updating an existing one, so a task's full
    /// progress history is preserved.
    /// </summary>
    [Fact]
    public async Task AddProgressAsync_CalledTwice_InsertsTwoRows()
    {
        var (taskId, _, _, _) = SeedFullTask();
        var service = CreateService();

        await service.AddProgressAsync(Guid.NewGuid(), taskId, 1000, 10, "{}", CancellationToken.None);
        await service.AddProgressAsync(Guid.NewGuid(), taskId, 2000, 20, "{}", CancellationToken.None);

        using var tenantDb = CreateTenantDbContext();
        var count = await tenantDb.TaskProgresses.CountAsync(p => p.TaskId == taskId);
        count.Should().Be(2);
    }
}

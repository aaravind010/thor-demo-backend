using System.Text.Json;
using Microsoft.Extensions.Logging;
using Thor.DataLayer.Models.Tenants;
using Thor.DataLayer.Repositories;
using Thor.Workflows.Ingestion.Models;

namespace Thor.Workflows.Ingestion.Staging;

/// <summary>
/// Bulk-loads a normalized <see cref="IngestBatch"/> into the transient staging tables
/// scoped to one run/batch.
///
/// <c>staging_account</c>/<c>staging_grp</c>/<c>staging_entitlement</c> map several text
/// columns as EF-required, but normalization can legitimately produce null for several of
/// them (e.g. no <c>mail</c> attribute). Coalesced to "" here as a documented, lossy
/// workaround pending the schema owner making those columns nullable — <c>RawAttributes</c>/
/// <c>ContentHash</c> are unaffected, since they already carry the true value.
/// </summary>
public sealed class Stager(
    IStagingRepository<StagingAccount> stagingAccounts,
    IStagingRepository<StagingGrp> stagingGrps,
    IStagingRepository<StagingAsset> stagingAssets,
    IStagingRepository<StagingEntitlement> stagingEntitlements,
    ILogger<Stager> logger)
{
    public async Task StageAsync(IngestBatch batch, Guid tenantId, Guid scanManifestId, int batchSeq, CancellationToken cancellationToken = default)
    {
        var receivedAt = DateTimeOffset.UtcNow;

        if (batch.Accounts.Count > 0)
        {
            var accountRows = batch.Accounts.Select(a => ToStagingAccount(a, scanManifestId, batchSeq, receivedAt));
            await stagingAccounts.BulkInsertAsync(accountRows, cancellationToken);
            logger.LogInformation("Staged {AccountCount} account(s) for scan manifest {ScanManifestId} batch {BatchSeq}", batch.Accounts.Count, scanManifestId, batchSeq);
        }

        if (batch.Groups.Count > 0)
        {
            var groupRows = batch.Groups.Select(g => ToStagingGrp(g, tenantId, scanManifestId, batchSeq, receivedAt));
            await stagingGrps.BulkInsertAsync(groupRows, cancellationToken);
            logger.LogInformation("Staged {GroupCount} group(s) for scan manifest {ScanManifestId} batch {BatchSeq}", batch.Groups.Count, scanManifestId, batchSeq);
        }

        if (batch.Assets.Count > 0)
        {
            var assetRows = batch.Assets.Select(a => ToStagingAsset(a, scanManifestId, batchSeq, receivedAt));
            await stagingAssets.BulkInsertAsync(assetRows, cancellationToken);
            logger.LogInformation("Staged {AssetCount} asset(s) for scan manifest {ScanManifestId} batch {BatchSeq}", batch.Assets.Count, scanManifestId, batchSeq);
        }

        if (batch.Entitlements.Count > 0)
        {
            var entitlementRows = batch.Entitlements.Select(e => ToStagingEntitlement(e, scanManifestId, batchSeq, receivedAt));
            await stagingEntitlements.BulkInsertAsync(entitlementRows, cancellationToken);
            logger.LogInformation("Staged {EntitlementCount} entitlement(s) for scan manifest {ScanManifestId} batch {BatchSeq}", batch.Entitlements.Count, scanManifestId, batchSeq);
        }
    }

    private static StagingAccount ToStagingAccount(ParsedAccount account, Guid scanManifestId, int batchSeq, DateTimeOffset receivedAt) => new()
    {
        ScanManifestId = scanManifestId,
        BatchSeq = batchSeq,
        SourceId = account.SourceId,
        ConnectorType = account.ConnectorType,
        NativeId = account.NativeId,
        AccountKind = account.AccountKind,
        IsHuman = account.IsHuman,
        DisplayName = account.DisplayName ?? "",
        SamAccountName = account.SamAccountName ?? "",
        Upn = account.Upn ?? "",
        Email = account.Email ?? "",
        DomainName = account.DomainName ?? "",
        FilerName = "", // AD never populates this — file-share connectors only
        NativeAccountId = account.NativeAccountId ?? "",
        IsDeleted = account.IsDeleted,
        IsDisabled = account.IsDisabled,
        RawAttributes = JsonSerializer.Serialize(account.RawAttributes),
        ContentHash = account.ContentHash,
        ReceivedAt = receivedAt,
    };

    private static StagingGrp ToStagingGrp(ParsedGroup group, Guid tenantId, Guid scanManifestId, int batchSeq, DateTimeOffset receivedAt) => new()
    {
        ScanManifestId = scanManifestId,
        BatchSeq = batchSeq,
        TenantId = tenantId,
        SourceId = group.SourceId,
        ConnectorType = group.ConnectorType,
        NativeId = group.NativeId,
        GroupClass = group.GroupClass,
        DisplayName = group.DisplayName ?? "",
        Email = group.Email ?? "",
        DomainName = group.DomainName ?? "",
        IsLargeGroup = group.IsLargeGroup,
        IsDeleted = group.IsDeleted,
        RawAttributes = JsonSerializer.Serialize(group.RawAttributes),
        ContentHash = group.ContentHash,
        ReceivedAt = receivedAt,
    };

    private static StagingAsset ToStagingAsset(ParsedAsset asset, Guid scanManifestId, int batchSeq, DateTimeOffset receivedAt) => new()
    {
        ScanManifestId = scanManifestId,
        BatchSeq = batchSeq,
        SourceId = asset.SourceId,
        ConnectorType = asset.ConnectorType,
        NativeId = asset.NativeId,
        AssetType = asset.AssetType,
        DisplayName = asset.DisplayName ?? "",
        FullPath = asset.FullPath ?? "",
        FilerName = asset.FilerName ?? "",
        FileSize = asset.FileSize ?? 0,
        FileCount = asset.FileCount ?? 0,
        BrokenAcl = asset.BrokenAcl ?? false,
        IsProtected = asset.IsProtected ?? false,
        RawAttributes = JsonSerializer.Serialize(asset.RawAttributes),
        ContentHash = asset.ContentHash,
        ReceivedAt = receivedAt,
    };

    private static StagingEntitlement ToStagingEntitlement(ParsedEntitlement entitlement, Guid scanManifestId, int batchSeq, DateTimeOffset receivedAt) => new()
    {
        ScanManifestId = scanManifestId,
        BatchSeq = batchSeq,
        SourceId = entitlement.SourceId,
        ConnectorType = entitlement.ConnectorType,
        NativeId = entitlement.NativeId,
        EntitlementType = entitlement.EntitlementType,
        Name = entitlement.Name ?? "",
        Description = entitlement.Description ?? "",
        IsAdmin = entitlement.IsAdmin,
        Scope = entitlement.Scope ?? "",
        InstanceName = entitlement.InstanceName ?? "",
        SudoPath = entitlement.SudoPath ?? "",
        SudoHost = entitlement.SudoHost ?? "",
        RawAttributes = JsonSerializer.Serialize(entitlement.RawAttributes),
        ContentHash = entitlement.ContentHash,
        ReceivedAt = receivedAt,
    };
}

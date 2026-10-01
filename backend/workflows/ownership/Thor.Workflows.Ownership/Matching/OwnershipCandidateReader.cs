using System.Runtime.CompilerServices;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using Thor.DataLayer.Data;
using Thor.Workflows.Ownership.Rules;

namespace Thor.Workflows.Ownership.Matching;

internal sealed record OwnershipCandidateRow(Guid EntityId, Guid IdentityId, Guid RuleId, decimal Weight);

/// <summary>
/// Streams one window of one entity-type phase's candidate-matching query row by row via a raw
/// <see cref="NpgsqlDataReader"/>, never materializing the whole result set — mirrors the POC's
/// dedicated-cursor design. The query itself is generated per call by
/// <see cref="OwnershipCandidateSqlBuilder"/> from whatever active rules were loaded for this
/// entity type; there is no hand-written SQL here.
/// </summary>
internal static class OwnershipCandidateReader
{
    public static async IAsyncEnumerable<OwnershipCandidateRow> StreamAsync(
        TenantDbContext db, string entityType, IReadOnlyList<ParsedOwnershipRule> rules, Guid? scanManifestId,
        string assignmentScope, string runId, int offset, int limit,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var (sql, parameters) = OwnershipCandidateSqlBuilder.Build(
            entityType, rules, scanManifestId, assignmentScope, runId, offset, limit);

        await db.Database.OpenConnectionAsync(cancellationToken);
        try
        {
            await using var command = CreateCommand(db, sql, parameters);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                yield return new OwnershipCandidateRow(
                    EntityId: reader.GetGuid(0),
                    IdentityId: reader.GetGuid(1),
                    RuleId: reader.GetGuid(2),
                    Weight: reader.GetDecimal(3));
            }
        }
        finally
        {
            await db.Database.CloseConnectionAsync();
        }
    }

    /// <summary>How many in-scope entities the window holds — see <see cref="OwnershipCandidateSqlBuilder.BuildWindowCount"/>.</summary>
    public static async Task<int> CountWindowAsync(
        TenantDbContext db, string entityType, Guid? scanManifestId, string assignmentScope, string runId, int offset, int limit,
        CancellationToken cancellationToken = default)
    {
        var (sql, parameters) = OwnershipCandidateSqlBuilder.BuildWindowCount(
            entityType, scanManifestId, assignmentScope, runId, offset, limit);

        await db.Database.OpenConnectionAsync(cancellationToken);
        try
        {
            await using var command = CreateCommand(db, sql, parameters);
            return (int)(await command.ExecuteScalarAsync(cancellationToken))!;
        }
        finally
        {
            await db.Database.CloseConnectionAsync();
        }
    }

    private static NpgsqlCommand CreateCommand(TenantDbContext db, string sql, IReadOnlyList<NpgsqlParameter> parameters)
    {
        var connection = (NpgsqlConnection)db.Database.GetDbConnection();
        var transaction = (NpgsqlTransaction?)db.Database.CurrentTransaction?.GetDbTransaction();
        var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddRange(parameters.ToArray());
        return command;
    }
}

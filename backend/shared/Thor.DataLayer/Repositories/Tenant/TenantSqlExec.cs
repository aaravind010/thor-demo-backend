using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using Thor.DataLayer.Data;

namespace Thor.DataLayer.Repositories;

/// <summary>
/// Raw-SQL execution helper for multi-row upserts EF's LINQ provider has no translation for
/// (<c>INSERT ... ON CONFLICT ... DO NOTHING RETURNING ...</c>) — mirrors the open/close-around-
/// each-call convention in <c>Thor.Workflows.Ingestion.EdgeGate.SqlExec</c> (EF Core ref-counts
/// <c>OpenConnectionAsync</c>/<c>CloseConnectionAsync</c>, so this is a no-op inside an ambient
/// transaction and the thing that makes a standalone caller actually open the connection
/// otherwise).
/// </summary>
internal static class TenantSqlExec
{
    public static async Task<List<T>> QueryAsync<T>(
        TenantDbContext db, string sql, Func<NpgsqlDataReader, T> map, IReadOnlyList<NpgsqlParameter> parameters,
        CancellationToken cancellationToken = default)
    {
        await db.Database.OpenConnectionAsync(cancellationToken);
        try
        {
            var connection = (NpgsqlConnection)db.Database.GetDbConnection();
            var transaction = (NpgsqlTransaction?)db.Database.CurrentTransaction?.GetDbTransaction();
            await using var command = new NpgsqlCommand(sql, connection, transaction);
            command.Parameters.AddRange(parameters.ToArray());

            var results = new List<T>();
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                results.Add(map(reader));
            }
            return results;
        }
        finally
        {
            await db.Database.CloseConnectionAsync();
        }
    }

    public static async Task ExecAsync(
        TenantDbContext db, string sql, IReadOnlyList<NpgsqlParameter> parameters, CancellationToken cancellationToken = default)
    {
        await db.Database.OpenConnectionAsync(cancellationToken);
        try
        {
            var connection = (NpgsqlConnection)db.Database.GetDbConnection();
            var transaction = (NpgsqlTransaction?)db.Database.CurrentTransaction?.GetDbTransaction();
            await using var command = new NpgsqlCommand(sql, connection, transaction);
            command.Parameters.AddRange(parameters.ToArray());
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        finally
        {
            await db.Database.CloseConnectionAsync();
        }
    }
}

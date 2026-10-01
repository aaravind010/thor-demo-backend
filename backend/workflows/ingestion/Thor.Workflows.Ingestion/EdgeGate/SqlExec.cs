using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using Thor.DataLayer.Data;

namespace Thor.Workflows.Ingestion.EdgeGate;

/// <summary>
/// Thin raw-SQL execution helpers shared by <see cref="EdgeResolver"/>/<see cref="OneSidedRefIndex"/>/
/// <see cref="EdgeRefDeltaComputer"/> — all run hand-written SQL directly against the ambient EF
/// connection/transaction rather than through LINQ (no translation exists for the CTE/upsert/
/// temp-table shapes they need). Each call is one statement: Postgres's extended query protocol
/// (used whenever parameters are bound) rejects multiple `;`-separated statements in a single
/// command, so multi-step operations issue one command per step rather than one semicolon-joined
/// command text.
///
/// <see cref="ExecAsync"/>/<see cref="ScalarLongAsync"/> explicitly open/close the connection
/// around each call via <c>Database.OpenConnectionAsync</c> — EF Core ref-counts this (it only
/// actually opens/closes on the outermost call), so it's a no-op when a caller (e.g.
/// <see cref="EdgeResolver"/>, inside its own <c>BeginTransactionAsync</c>) already holds the
/// connection open, and the one thing that makes a standalone caller (e.g.
/// <see cref="EdgeRefDeltaComputer"/>, which isn't wrapped in a transaction) actually work —
/// a raw <see cref="NpgsqlCommand"/> against <c>GetDbConnection()</c> does not auto-open it.
/// </summary>
internal static class SqlExec
{
    public static (NpgsqlConnection Connection, NpgsqlTransaction? Transaction) Conn(TenantDbContext db) =>
        ((NpgsqlConnection)db.Database.GetDbConnection(), (NpgsqlTransaction?)db.Database.CurrentTransaction?.GetDbTransaction());

    public static async Task ExecAsync(TenantDbContext db, string sql, CancellationToken cancellationToken, params NpgsqlParameter[] parameters)
    {
        await db.Database.OpenConnectionAsync(cancellationToken);
        try
        {
            var (connection, transaction) = Conn(db);
            await using var command = new NpgsqlCommand(sql, connection, transaction);
            command.Parameters.AddRange(parameters);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        finally
        {
            await db.Database.CloseConnectionAsync();
        }
    }

    public static async Task<long> ScalarLongAsync(TenantDbContext db, string sql, CancellationToken cancellationToken, params NpgsqlParameter[] parameters)
    {
        await db.Database.OpenConnectionAsync(cancellationToken);
        try
        {
            var (connection, transaction) = Conn(db);
            await using var command = new NpgsqlCommand(sql, connection, transaction);
            command.Parameters.AddRange(parameters);
            return (long)(await command.ExecuteScalarAsync(cancellationToken) ?? 0L);
        }
        finally
        {
            await db.Database.CloseConnectionAsync();
        }
    }
}

using Npgsql;
using NpgsqlTypes;
using Testcontainers.PostgreSql;
using Thor.Workflows.Ingestion.EdgeGate;
using Thor.Workflows.Ingestion.Hashing;
using Xunit;

namespace Thor.Workflows.Ingestion.Tests.Hashing;

/// <summary>
/// Pins <see cref="ContentHasher.EdgeHash"/> byte-for-byte against <see cref="OwnerRefsSql.HashExpr"/>
/// — the SQL expression <see cref="EdgeGate.EdgeResolver"/> actually runs in Postgres to compute the
/// same hash. The two must never drift: CDC only ever diffs a hash against its own prior value, so a
/// silent divergence would go undetected until cross-checked against Postgres directly, which is what
/// this test does on every run. Mirrors the POC's own <c>test_edge_hash_parity.py</c>.
/// </summary>
public sealed class EdgeHashParityTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder().WithImage("postgres:16-alpine").Build();
    private NpgsqlConnection _connection = null!;

    public async Task InitializeAsync()
    {
        await _container.StartAsync();
        _connection = new NpgsqlConnection(_container.GetConnectionString());
        await _connection.OpenAsync();
    }

    public async Task DisposeAsync()
    {
        await _connection.DisposeAsync();
        await _container.DisposeAsync();
    }

    public static IEnumerable<object?[]> Fixtures()
    {
        yield return [Guid.Parse("11111111-1111-1111-1111-111111111111"), Guid.Parse("22222222-2222-2222-2222-222222222222"), "MEMBER_OF", null];
        yield return [Guid.Parse("33333333-3333-3333-3333-333333333333"), Guid.Parse("44444444-4444-4444-4444-444444444444"), "HAS_ACCESS", """{"is_admin":true}"""];
        yield return [Guid.Parse("55555555-5555-5555-5555-555555555555"), Guid.Parse("66666666-6666-6666-6666-666666666666"), "HAS_ACCESS", """{"is_admin":false,"read":true,"write":false}"""];
        yield return [Guid.Parse("77777777-7777-7777-7777-777777777777"), Guid.Parse("88888888-8888-8888-8888-888888888888"), "REPORTS_TO", "{}"];
    }

    [Theory]
    [MemberData(nameof(Fixtures))]
    public async Task EdgeHash_MatchesSqlHashExpression(Guid fromId, Guid toId, string relType, string? props)
    {
        var csharpHash = ContentHasher.EdgeHash(fromId, toId, relType, props);

        var sql = $"""
            SELECT {OwnerRefsSql.HashExpr}
            FROM (SELECT @from_id::uuid AS from_id, @to_id::uuid AS to_id, @rel_type::text AS rel_type, @props::text AS props) t
            """;
        await using var command = new NpgsqlCommand(sql, _connection);
        command.Parameters.AddWithValue("from_id", fromId);
        command.Parameters.AddWithValue("to_id", toId);
        command.Parameters.AddWithValue("rel_type", relType);
        command.Parameters.Add(new NpgsqlParameter("props", NpgsqlDbType.Text) { Value = (object?)props ?? DBNull.Value });
        var sqlHash = (string)(await command.ExecuteScalarAsync())!;

        Assert.Equal(sqlHash, csharpHash);
    }
}

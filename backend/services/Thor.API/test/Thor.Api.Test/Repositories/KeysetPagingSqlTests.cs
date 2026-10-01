using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Thor.DataLayer.Data;
using Thor.DataLayer.Repositories;

namespace Thor.Api.Test.Repositories;

/// <summary>
/// The in-memory provider can't prove SQL translation, so this builds the keyset query against the
/// real Npgsql provider (no connection is opened by <c>ToQueryString</c>) and checks its shape.
/// </summary>
public class KeysetPagingSqlTests
{
    private static TenantDbContext CreateNpgsqlContext() =>
        new(new DbContextOptionsBuilder<TenantDbContext>()
            .UseNpgsql("Host=localhost;Database=unused")
            .Options);

    /// <summary>A cursor becomes an index-friendly, parameterized <c>id &gt; @p</c> predicate with ORDER BY + LIMIT.</summary>
    [Fact]
    public void KeysetWindow_WithCursor_TranslatesToParameterizedGreaterThan()
    {
        using var db = CreateNpgsqlContext();

        var sql = db.Accounts.KeysetWindow(a => a.Id, Guid.NewGuid(), 50).ToQueryString();

        sql.Should().MatchRegex(@"WHERE a\.id > @\w+");
        sql.Should().Contain("ORDER BY a.id");
        sql.Should().Contain("LIMIT @");
    }

    /// <summary>The first page has no cursor predicate at all.</summary>
    [Fact]
    public void KeysetWindow_WithoutCursor_HasNoWherePredicate()
    {
        using var db = CreateNpgsqlContext();

        var sql = db.Accounts.KeysetWindow(a => a.Id, null, 50).ToQueryString();

        sql.Should().NotContain("WHERE");
        sql.Should().Contain("ORDER BY a.id");
    }
}

using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;

namespace Thor.DataLayer.Repositories;

/// <summary>One page of a keyset-paged query; <see cref="NextCursor"/> is null on the last page.</summary>
public sealed record KeysetPage<T>(IReadOnlyList<T> Items, Guid? NextCursor);

/// <summary>
/// Keyset (cursor) paging over a <see cref="Guid"/> key: <c>WHERE key &gt; @after ORDER BY key LIMIT n</c>.
/// Stays index-backed at any depth, unlike OFFSET, so it is safe on the large account/vote tables.
/// </summary>
public static class KeysetPaging
{
    public static async Task<KeysetPage<T>> ToKeysetPageAsync<T>(
        this IQueryable<T> query,
        Expression<Func<T, Guid>> key,
        Guid? after,
        int limit,
        CancellationToken cancellationToken = default)
    {
        var rows = await query.KeysetWindow(key, after, limit).ToListAsync(cancellationToken);
        if (rows.Count <= limit)
        {
            return new KeysetPage<T>(rows, null);
        }

        rows.RemoveAt(limit);
        return new KeysetPage<T>(rows, key.Compile()(rows[^1]));
    }

    /// <summary>The untranslated page query — <c>limit + 1</c> rows after <paramref name="after"/>; the extra row signals another page exists without a <c>COUNT(*)</c>.</summary>
    public static IQueryable<T> KeysetWindow<T>(this IQueryable<T> query, Expression<Func<T, Guid>> key, Guid? after, int limit)
    {
        if (after is { } cursor)
        {
            query = query.Where(After(key, cursor));
        }

        return query.OrderBy(key).Take(limit + 1);
    }

    // Builds `x => key(x).CompareTo(cursor) > 0`, reading the cursor through a member access so EF
    // sends it as a SQL parameter rather than inlining a literal per distinct cursor value.
    private static Expression<Func<T, bool>> After<T>(Expression<Func<T, Guid>> key, Guid cursor)
    {
        var holder = Expression.Property(Expression.Constant(new CursorHolder(cursor)), nameof(CursorHolder.Value));
        var compareTo = Expression.Call(key.Body, typeof(Guid).GetMethod(nameof(Guid.CompareTo), [typeof(Guid)])!, holder);
        return Expression.Lambda<Func<T, bool>>(Expression.GreaterThan(compareTo, Expression.Constant(0)), key.Parameters);
    }

    private sealed class CursorHolder(Guid value)
    {
        public Guid Value { get; } = value;
    }
}

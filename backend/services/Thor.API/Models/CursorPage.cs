namespace Thor.Api.Models;

/// <summary>
/// One page of a keyset-paged list. Pass <see cref="NextCursor"/> back as <c>?after=</c> to read
/// the next page; it is null on the last page.
/// </summary>
public sealed record CursorPage<T>(IReadOnlyList<T> Items, Guid? NextCursor);

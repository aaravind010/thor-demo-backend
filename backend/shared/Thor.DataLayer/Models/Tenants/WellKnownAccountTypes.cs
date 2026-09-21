namespace Thor.DataLayer.Models.Tenants;

/// <summary>
/// Fixed <see cref="AccountType"/> ids seeded into every tenant database by migration, rather
/// than looked up by name — <see cref="Account.AccountTypeId"/> is a non-null FK with no
/// classification logic wired up yet (that's the separate, not-yet-built ATRE workflow), so
/// promotion needs a valid default to point new accounts at until ATRE assigns a real type.
/// </summary>
public static class WellKnownAccountTypes
{
    public static readonly Guid Unclassified = Guid.Parse("00000000-0000-0000-0000-000000000001");
}

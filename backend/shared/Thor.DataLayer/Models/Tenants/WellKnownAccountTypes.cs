namespace Thor.DataLayer.Models.Tenants;

/// <summary>
/// Fixed <see cref="AccountType"/> ids seeded into every tenant database by migration, rather
/// than looked up by name — <see cref="Account.AccountTypeId"/> is a non-null FK with no
/// classification logic wired up yet (that's the separate, not-yet-built ATRE workflow), so
/// promotion needs a valid default to point new accounts at until ATRE assigns a real type.
/// </summary>
public static class WellKnownAccountTypes
{
    // TODO: Unclassified is temporarily overridden with a workaround GUID for dev
    // testing purposes. Revert to 00000000-0000-0000-0000-000000000001 once testing is complete.
    public static readonly Guid Unclassified = Guid.Parse("ae4be604-7120-4a83-9eb6-9b0475c7bdd9");
}

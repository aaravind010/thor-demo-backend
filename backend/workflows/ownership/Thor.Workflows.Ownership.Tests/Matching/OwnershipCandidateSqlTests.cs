using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Testcontainers.PostgreSql;
using Thor.DataLayer.Data;
using Thor.DataLayer.Models.Tenants;
using Thor.Workflows.Ownership.Constants;
using Thor.Workflows.Ownership.Matching;
using Thor.Rules.Ownership;
using Thor.Workflows.Ownership.Rules;
using Thor.Workflows.Ownership.Tests.Fixtures;
using Thor.Workflows.Ownership.Walk;
using Xunit;

namespace Thor.Workflows.Ownership.Tests.Matching;

/// <summary>
/// Exercises the generic rule engine (<see cref="OwnershipRuleLoader"/> →
/// <see cref="OwnershipCandidateSqlBuilder"/> via <see cref="OwnershipCandidateReader"/>) against a
/// real Postgres (Testcontainers), one entity-type phase at a time — this is generated SQL with no
/// LINQ equivalent, so it's verified directly rather than only through the full
/// <c>OwnershipVoteStep</c>.
/// </summary>
public sealed class OwnershipCandidateSqlTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .Build();

    private Guid _sourceId;
    private TenantDbContext _context = null!;

    public async Task InitializeAsync()
    {
        await _container.StartAsync();
        _context = NewContext();
        await _context.Database.EnsureCreatedAsync();

        _context.AccountTypes.Add(new AccountType
        {
            Id = WellKnownAccountTypes.Unclassified, Name = "Unclassified", Description = "d", IsHuman = false,
        });
        _context.OwnershipRules.AddRange(OwnershipSeedData.BuildRules());

        _sourceId = Guid.NewGuid();
        _context.Sources.Add(new Source
        {
            Id = _sourceId, ConnectorType = 308, Name = "s", Config = "{}", IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow,
        });
        await _context.SaveChangesAsync();
    }

    public async Task DisposeAsync()
    {
        await _context.DisposeAsync();
        await _container.DisposeAsync();
    }

    private TenantDbContext NewContext() =>
        new(new DbContextOptionsBuilder<TenantDbContext>().UseNpgsql(_container.GetConnectionString()).Options);

    private async Task<Account> AddAccountAsync(
        string nativeId, string? email = null, string? upn = null, string? displayName = null, string? department = null)
    {
        var account = OwnershipSeedData.BuildAccount(_sourceId, nativeId, email, upn, displayName, department);
        _context.Accounts.Add(account);
        await _context.SaveChangesAsync();
        return account;
    }

    private async Task<IdentityRecord> AddIdentityAsync(string hrId, string displayName, string email, string? department = null)
    {
        var identity = OwnershipSeedData.BuildIdentity(hrId, displayName, email, department);
        _context.Identities.Add(identity);
        await _context.SaveChangesAsync();
        return identity;
    }

    private async Task AddPartyAssignmentAsync(
        string entityType, Guid entityId, Guid identityId, int rank = 1, bool isActive = true, bool isOverride = false)
    {
        _context.PartyAssignments.Add(new PartyAssignment
        {
            Id = Guid.NewGuid(), EntityType = entityType, EntityId = entityId, IdentityId = identityId,
            Rank = rank, IsActive = isActive, IsOverride = isOverride, VoteDistribution = "{}",
            ContributingRuleIds = [], PrecisionScoreSnapshot = "{}", RunId = "prior-run",
            AssignedAt = DateTimeOffset.UtcNow,
        });
        await _context.SaveChangesAsync();
    }

    /// <summary>
    /// One phase, the way a run does it: the walk step stages the phase's walk-rule candidates
    /// first, then a single window covering every entity reads them back with the other rules.
    /// </summary>
    private async Task<List<OwnershipCandidateRow>> RunAsync(
        string entityType = "account", Guid? scanManifestId = null, string assignmentScope = OwnershipAssignmentScope.UnassignedAndConfirmed)
    {
        var runId = Guid.NewGuid().ToString();
        var rules = await OwnershipRuleLoader.LoadActiveAsync(_context, entityType, NullLogger.Instance);

        await new OwnershipWalkPropagator(_context, runId, NullLogger.Instance)
            .RunAsync([.. rules.Where(OwnershipWalkPropagator.IsWalkRule)], TimeSpan.MaxValue, CancellationToken.None);

        var results = new List<OwnershipCandidateRow>();
        await foreach (var row in OwnershipCandidateReader.StreamAsync(
            _context, entityType, rules, scanManifestId, assignmentScope, runId, offset: 0, limit: int.MaxValue))
        {
            results.Add(row);
        }
        return results;
    }

    private async Task<Guid> RuleIdAsync(string ruleName) =>
        (await _context.OwnershipRules.AsNoTracking().SingleAsync(r => r.RuleName == ruleName)).Id;

    [Fact]
    public async Task Rule1_EmailExactMatch_Fires()
    {
        var account = await AddAccountAsync("alice", email: "alice@corp.com");
        var identity = await AddIdentityAsync("E1", "Alice Smith", "alice@corp.com");

        var results = await RunAsync();

        var row = Assert.Single(results);
        Assert.Equal(account.Id, row.EntityId);
        Assert.Equal(identity.Id, row.IdentityId);
        Assert.Equal(await RuleIdAsync(OwnershipSeedData.EmailExactRule), row.RuleId);
        Assert.Equal(0.90m, row.Weight);
    }

    [Fact]
    public async Task Rule2_ManagerAttribute_Fires()
    {
        var manager = await AddAccountAsync("mgr", email: "manager@corp.com");
        var account = await AddAccountAsync("svc-backup01", email: "svc-backup01@corp.local");
        _context.Edges.Add(OwnershipSeedData.BuildEdge(account.Id, "account", manager.Id, "account", "REPORTS_TO"));
        await _context.SaveChangesAsync();
        var identity = await AddIdentityAsync("E2", "Manager Person", "manager@corp.com");

        var results = await RunAsync();

        // The manager account itself also gets proposed via rule 1 (its own email exact-matches
        // the identity) — a correct, independent side effect, not something this test targets.
        Assert.Equal(2, results.Count);
        var row = Assert.Single(results, r => r.EntityId == account.Id);
        Assert.Equal(identity.Id, row.IdentityId);
        Assert.Equal(await RuleIdAsync(OwnershipSeedData.ManagerAttributeRule), row.RuleId);
        Assert.Equal(0.70m, row.Weight);

        var managerRow = Assert.Single(results, r => r.EntityId == manager.Id);
        Assert.Equal(await RuleIdAsync(OwnershipSeedData.EmailExactRule), managerRow.RuleId);
    }

    [Fact]
    public async Task Rule3_UpnCorrelation_Fires()
    {
        await AddAccountAsync("bob", upn: "bob@corp.local");
        var identity = await AddIdentityAsync("E3", "Bob Jones", "bob@othersuffix.com");

        var results = await RunAsync();

        var row = Assert.Single(results);
        Assert.Equal(identity.Id, row.IdentityId);
        Assert.Equal(0.65m, row.Weight);
    }

    [Fact]
    public async Task Rule4_DisplayNameMatch_Fires()
    {
        await AddAccountAsync("carol", displayName: "Carol Danvers");
        var identity = await AddIdentityAsync("E4", "Carol Danvers", "carol.danvers@corp.com");

        var results = await RunAsync();

        var row = Assert.Single(results);
        Assert.Equal(identity.Id, row.IdentityId);
        Assert.Equal(0.55m, row.Weight);
    }

    [Fact]
    public async Task Rule5_DepartmentFallback_FiresForEveryoneInDepartment()
    {
        await AddAccountAsync("dave", department: "IT");
        var identity1 = await AddIdentityAsync("E5", "Person One", "one@corp.com", department: "IT");
        var identity2 = await AddIdentityAsync("E6", "Person Two", "two@corp.com", department: "IT");
        await AddIdentityAsync("E7", "Person Three", "three@corp.com", department: "Sales");

        var results = await RunAsync();

        Assert.Equal(2, results.Count);
        Assert.All(results, r => Assert.Equal(0.40m, r.Weight));
        Assert.Contains(results, r => r.IdentityId == identity1.Id);
        Assert.Contains(results, r => r.IdentityId == identity2.Id);
    }

    [Fact]
    public async Task DeptCandidateCap_KeepsExactlyCapCount_WhenMoreExistInDepartment()
    {
        await AddAccountAsync("eve", department: "IT");
        for (var i = 0; i < OwnershipRuleDefaults.DepartmentFallbackCap + 3; i++)
        {
            await AddIdentityAsync($"E{i}", $"Person {i}", $"p{i}@corp.com", department: "IT");
        }

        var results = await RunAsync();

        Assert.Equal(OwnershipRuleDefaults.DepartmentFallbackCap, results.Count);
    }

    [Fact]
    public async Task MultipleRulesMatchSameIdentity_HighestWeightRuleWins()
    {
        // Same identity matches both rule 1 (email exact) and rule 4 (display name) for this account.
        await AddAccountAsync("frank", email: "frank@corp.com", displayName: "Frank Castle");
        var identity = await AddIdentityAsync("E8", "Frank Castle", "frank@corp.com");

        var results = await RunAsync();

        var row = Assert.Single(results); // one row, not two — the collapse keeps only the higher-weight rule
        Assert.Equal(identity.Id, row.IdentityId);
        Assert.Equal(0.90m, row.Weight);
    }

    [Fact]
    public async Task DeptFallbackCandidateAlsoMatchingStrongerRule_CollapsesToStrongerRule()
    {
        // Same department AND same email — rule 1 should win, not produce a second dept-fallback row.
        await AddAccountAsync("grace", email: "grace@corp.com", department: "IT");
        await AddIdentityAsync("E9", "Grace Hopper", "grace@corp.com", department: "IT");

        var results = await RunAsync();

        var row = Assert.Single(results);
        Assert.Equal(0.90m, row.Weight);
    }

    [Fact]
    public async Task TwoRulesMatchDifferentIdentitiesForSameAccount_BothCandidatesKept_RankedByWeight()
    {
        await AddAccountAsync("henry", email: "henry@corp.com", department: "IT");
        var emailMatch = await AddIdentityAsync("E10", "Henry Exact", "henry@corp.com", department: "Sales");
        var deptMatch = await AddIdentityAsync("E11", "Someone Else", "other@corp.com", department: "IT");

        var results = await RunAsync();

        Assert.Equal(2, results.Count);
        Assert.Equal(emailMatch.Id, results[0].IdentityId); // weight DESC — rule 1 (0.90) before rule 5 (0.40)
        Assert.Equal(0.90m, results[0].Weight);
        Assert.Equal(deptMatch.Id, results[1].IdentityId);
        Assert.Equal(0.40m, results[1].Weight);
    }

    [Fact]
    public async Task EmptyEmail_NeverMatchesRule1()
    {
        await AddAccountAsync("irene", email: "");
        await AddIdentityAsync("E12", "Irene Adler", "");

        var results = await RunAsync();

        Assert.Empty(results);
    }

    [Fact]
    public async Task ZeroRulesFire_NoRowsReturned()
    {
        await AddAccountAsync("jack", email: "jack@corp.com", displayName: "Jack Sparrow");
        await AddIdentityAsync("E13", "Someone Unrelated", "unrelated@corp.com", department: "Legal");

        var results = await RunAsync();

        Assert.Empty(results);
    }

    [Fact]
    public async Task AccountAlreadyOwned_ExcludedFromScoping()
    {
        var account = await AddAccountAsync("karen", email: "karen@corp.com");
        var identity = await AddIdentityAsync("E14", "Karen Page", "karen@corp.com");
        await AddPartyAssignmentAsync("account", account.Id, identity.Id);

        var results = await RunAsync();

        Assert.Empty(results);
    }

    [Fact]
    public async Task ScopeAll_IncludesUnassignedProposedAndConfirmedEntities()
    {
        var unassigned = await AddAccountAsync("scope-all-unassigned", email: "scopeall1@corp.com");
        await AddIdentityAsync("SA1", "Person One", "scopeall1@corp.com");

        var proposedAccount = await AddAccountAsync("scope-all-proposed", email: "scopeall2@corp.com");
        var proposedIdentity = await AddIdentityAsync("SA2", "Person Two", "scopeall2@corp.com");
        await AddPartyAssignmentAsync("account", proposedAccount.Id, proposedIdentity.Id, isOverride: false);

        var confirmedAccount = await AddAccountAsync("scope-all-confirmed", email: "scopeall3@corp.com");
        var confirmedIdentity = await AddIdentityAsync("SA3", "Person Three", "scopeall3@corp.com");
        await AddPartyAssignmentAsync("account", confirmedAccount.Id, confirmedIdentity.Id, isOverride: true);

        var results = await RunAsync(assignmentScope: OwnershipAssignmentScope.All);

        Assert.Equal(3, results.Count);
        Assert.Contains(results, r => r.EntityId == unassigned.Id);
        Assert.Contains(results, r => r.EntityId == proposedAccount.Id);
        Assert.Contains(results, r => r.EntityId == confirmedAccount.Id);
    }

    [Fact]
    public async Task ScopeUnassignedOnly_ExcludesBothProposedAndConfirmedEntities()
    {
        var unassigned = await AddAccountAsync("scope-uo-unassigned", email: "scopeuo1@corp.com");
        await AddIdentityAsync("SU1", "Person One", "scopeuo1@corp.com");

        var proposedAccount = await AddAccountAsync("scope-uo-proposed", email: "scopeuo2@corp.com");
        var proposedIdentity = await AddIdentityAsync("SU2", "Person Two", "scopeuo2@corp.com");
        await AddPartyAssignmentAsync("account", proposedAccount.Id, proposedIdentity.Id, isOverride: false);

        var confirmedAccount = await AddAccountAsync("scope-uo-confirmed", email: "scopeuo3@corp.com");
        var confirmedIdentity = await AddIdentityAsync("SU3", "Person Three", "scopeuo3@corp.com");
        await AddPartyAssignmentAsync("account", confirmedAccount.Id, confirmedIdentity.Id, isOverride: true);

        var results = await RunAsync(assignmentScope: OwnershipAssignmentScope.UnassignedOnly);

        var row = Assert.Single(results);
        Assert.Equal(unassigned.Id, row.EntityId);
    }

    [Fact]
    public async Task ScopeUnassignedAndProposed_ExcludesOnlyConfirmedEntities()
    {
        var unassigned = await AddAccountAsync("scope-uap-unassigned", email: "scopeuap1@corp.com");
        await AddIdentityAsync("SP1", "Person One", "scopeuap1@corp.com");

        var proposedAccount = await AddAccountAsync("scope-uap-proposed", email: "scopeuap2@corp.com");
        var proposedIdentity = await AddIdentityAsync("SP2", "Person Two", "scopeuap2@corp.com");
        await AddPartyAssignmentAsync("account", proposedAccount.Id, proposedIdentity.Id, isOverride: false);

        var confirmedAccount = await AddAccountAsync("scope-uap-confirmed", email: "scopeuap3@corp.com");
        var confirmedIdentity = await AddIdentityAsync("SP3", "Person Three", "scopeuap3@corp.com");
        await AddPartyAssignmentAsync("account", confirmedAccount.Id, confirmedIdentity.Id, isOverride: true);

        var results = await RunAsync(assignmentScope: OwnershipAssignmentScope.UnassignedAndProposed);

        Assert.Equal(2, results.Count);
        Assert.Contains(results, r => r.EntityId == unassigned.Id);
        Assert.Contains(results, r => r.EntityId == proposedAccount.Id);
        Assert.DoesNotContain(results, r => r.EntityId == confirmedAccount.Id);
    }

    [Fact]
    public async Task ScopeUnassignedAndConfirmed_ExcludesOnlyProposedEntities()
    {
        var unassigned = await AddAccountAsync("scope-uac-unassigned", email: "scopeuac1@corp.com");
        await AddIdentityAsync("SC1", "Person One", "scopeuac1@corp.com");

        var proposedAccount = await AddAccountAsync("scope-uac-proposed", email: "scopeuac2@corp.com");
        var proposedIdentity = await AddIdentityAsync("SC2", "Person Two", "scopeuac2@corp.com");
        await AddPartyAssignmentAsync("account", proposedAccount.Id, proposedIdentity.Id, isOverride: false);

        var confirmedAccount = await AddAccountAsync("scope-uac-confirmed", email: "scopeuac3@corp.com");
        var confirmedIdentity = await AddIdentityAsync("SC3", "Person Three", "scopeuac3@corp.com");
        await AddPartyAssignmentAsync("account", confirmedAccount.Id, confirmedIdentity.Id, isOverride: true);

        // Default scope (used implicitly by every other test in this file, and by OwnershipVoteStep) —
        // matches AccountAlreadyOwned_ExcludedFromScoping's behavior exactly.
        var results = await RunAsync(assignmentScope: OwnershipAssignmentScope.UnassignedAndConfirmed);

        Assert.Equal(2, results.Count);
        Assert.Contains(results, r => r.EntityId == unassigned.Id);
        Assert.Contains(results, r => r.EntityId == confirmedAccount.Id);
        Assert.DoesNotContain(results, r => r.EntityId == proposedAccount.Id);
    }

    [Fact]
    public async Task LivePrecisionScore_ReflectsTheSeededRuleRow()
    {
        // Explicit non-colliding upn: the default upn ("liam@corp.local") would otherwise also
        // fire rule 3 (UPN correlation) against this same identity's email, muddying this test.
        await AddAccountAsync("liam", email: "liam@corp.com", upn: "unrelated-upn@corp.local");
        await AddIdentityAsync("E15", "Liam Neeson", "liam@corp.com");
        var rule = await _context.OwnershipRules.SingleAsync(r => r.RuleName == OwnershipSeedData.EmailExactRule);
        rule.PrecisionScore = 0.6364m;
        await _context.SaveChangesAsync();

        var results = await RunAsync();

        var row = Assert.Single(results);
        Assert.Equal(0.6364m, row.Weight);
    }

    [Fact]
    public async Task InactiveRuleRow_NeverFires()
    {
        // Explicit non-colliding upn: the default upn ("mona@corp.local") would otherwise also
        // fire rule 3 (UPN correlation) against this same identity's email, muddying this test.
        await AddAccountAsync("mona", email: "mona@corp.com", upn: "unrelated-upn@corp.local");
        await AddIdentityAsync("E16", "Mona Lisa", "mona@corp.com");
        var rule = await _context.OwnershipRules.SingleAsync(r => r.RuleName == OwnershipSeedData.EmailExactRule);
        rule.IsActive = false;
        await _context.SaveChangesAsync();

        var results = await RunAsync();

        Assert.Empty(results); // is_active now genuinely gates whether a rule runs at all, not just its weight
    }

    [Fact]
    public async Task ManifestScoping_OnlyIncludesAccountsReferencedByThatManifest()
    {
        var scoped = await AddAccountAsync("nina", email: "nina@corp.com");
        var unscoped = await AddAccountAsync("oscar", email: "oscar@corp.com");
        await AddIdentityAsync("E17", "Nina Simone", "nina@corp.com");
        await AddIdentityAsync("E18", "Oscar Wilde", "oscar@corp.com");

        var (scanId, manifestId) = await SeedScanChainAsync();
        _context.IngestChangeEvents.Add(new IngestChangeEvent
        {
            Id = Guid.NewGuid(), ScanManifestId = manifestId, ScanId = scanId, EntityType = "account",
            EntityId = scoped.Id, ChangeType = "inserted", OccurredAt = DateTimeOffset.UtcNow,
        });
        await _context.SaveChangesAsync();

        var results = await RunAsync(scanManifestId: manifestId);

        var row = Assert.Single(results);
        Assert.Equal(scoped.Id, row.EntityId);
        Assert.DoesNotContain(results, r => r.EntityId == unscoped.Id);
    }

    [Fact]
    public async Task NoInScopeAccounts_ReturnsEmpty()
    {
        var (_, manifestId) = await SeedScanChainAsync();

        var results = await RunAsync(scanManifestId: manifestId);

        Assert.Empty(results);
    }

    [Fact]
    public async Task GroupManagedByInheritance_ResolvesToManagerAccountsOwner()
    {
        var manager = await AddAccountAsync("grp-mgr", email: "grpmgr@corp.com");
        var identity = await AddIdentityAsync("E19", "Group Manager", "grpmgr@corp.com");
        await AddPartyAssignmentAsync("account", manager.Id, identity.Id);

        var group = OwnershipSeedData.BuildGroup(_sourceId, "finance-team");
        _context.Grps.Add(group);
        _context.Edges.Add(OwnershipSeedData.BuildEdge(group.Id, "grp", manager.Id, "account", "MANAGED_BY"));
        await _context.SaveChangesAsync();

        var results = await RunAsync("grp");

        var row = Assert.Single(results);
        Assert.Equal(group.Id, row.EntityId);
        Assert.Equal(identity.Id, row.IdentityId);
        Assert.Equal(0.80m, row.Weight);
    }

    [Fact]
    public async Task GroupNestingInheritance_ResolvesToParentGroupsOwner()
    {
        var identity = await AddIdentityAsync("E20", "Parent Owner", "parentowner@corp.com");
        var parent = OwnershipSeedData.BuildGroup(_sourceId, "parent-group");
        var child = OwnershipSeedData.BuildGroup(_sourceId, "child-group");
        _context.Grps.AddRange(parent, child);
        await _context.SaveChangesAsync();

        await AddPartyAssignmentAsync("grp", parent.Id, identity.Id);
        _context.Edges.Add(OwnershipSeedData.BuildEdge(child.Id, "grp", parent.Id, "grp", "MEMBER_OF"));
        await _context.SaveChangesAsync();

        var results = await RunAsync("grp");

        var row = Assert.Single(results);
        Assert.Equal(child.Id, row.EntityId);
        Assert.Equal(identity.Id, row.IdentityId);
        Assert.Equal(0.75m, row.Weight);
    }

    [Fact]
    public async Task AssetParentInheritance_WalksUpToNearestOwnedAncestor()
    {
        var identity = await AddIdentityAsync("E21", "Share Owner", "shareowner@corp.com");
        var grandparent = OwnershipSeedData.BuildAsset(_sourceId, "root-share");
        _context.Assets.Add(grandparent);
        await _context.SaveChangesAsync();
        await AddPartyAssignmentAsync("asset", grandparent.Id, identity.Id);

        var parent = OwnershipSeedData.BuildAsset(_sourceId, "mid-folder", parentAssetId: grandparent.Id);
        var child = OwnershipSeedData.BuildAsset(_sourceId, "leaf-folder", parentAssetId: parent.Id);
        _context.Assets.AddRange(parent, child);
        await _context.SaveChangesAsync();

        var results = await RunAsync("asset");

        Assert.Equal(2, results.Count); // both parent and child inherit — grandparent itself is already owned, excluded from scope
        Assert.Contains(results, r => r.EntityId == parent.Id && r.IdentityId == identity.Id);
        Assert.Contains(results, r => r.EntityId == child.Id && r.IdentityId == identity.Id);
    }

    [Fact]
    public async Task SameAccountName_SiblingMatch_InheritsOwnerFromExactNameMatch()
    {
        var owned = await AddAccountAsync("svc-owned", displayName: "SharedName");
        var identity = await AddIdentityAsync("E30", "Some Owner", "someowner@corp.com");
        await AddPartyAssignmentAsync("account", owned.Id, identity.Id);

        var unowned = await AddAccountAsync("svc-unowned", displayName: "SharedName");

        var results = await RunAsync();

        var row = Assert.Single(results, r => r.EntityId == unowned.Id);
        Assert.Equal(identity.Id, row.IdentityId);
        Assert.Equal(0.50m, row.Weight);
    }

    [Fact]
    public async Task SimilarAccountName_SiblingMatch_MatchesDelimiterBoundedVariant()
    {
        // "jsmith_admin" is similar to "jsmith" — a delimiter-bounded variant of the same base name.
        var owned = await AddAccountAsync("jsmith", displayName: "jsmith");
        var identity = await AddIdentityAsync("E31", "J Smith", "jsmith-owner@corp.com");
        await AddPartyAssignmentAsync("account", owned.Id, identity.Id);

        var unowned = await AddAccountAsync("jsmith-admin", displayName: "jsmith_admin");

        var results = await RunAsync();

        var row = Assert.Single(results, r => r.EntityId == unowned.Id);
        Assert.Equal(identity.Id, row.IdentityId);
        Assert.Equal(0.35m, row.Weight);
    }

    [Fact]
    public async Task SimilarAccountName_NoDelimiter_DoesNotMatch()
    {
        // "jsmithadmin" has no delimiter separating it from "jsmith" — not a bounded variant, so not similar.
        var owned = await AddAccountAsync("jsmith2", displayName: "jsmith");
        var identity = await AddIdentityAsync("E32", "J Smith Two", "jsmith2owner@corp.com");
        await AddPartyAssignmentAsync("account", owned.Id, identity.Id);

        await AddAccountAsync("jsmithadmin", displayName: "jsmithadmin");

        var results = await RunAsync();

        Assert.Empty(results);
    }

    [Fact]
    public async Task EmployeeIdInAccountName_ContainsMatch_Fires()
    {
        var account = await AddAccountAsync("svc-12345-backup"); // sam_account_name and display_name both default to nativeId
        var identity = await AddIdentityAsync("12345", "Some Employee", "someemployee@corp.com");

        var results = await RunAsync();

        // Both the AccountName and DisplayName EmpID rules fire here (nativeId feeds both fields)
        // — the collapse keeps only the higher-weight one.
        var row = Assert.Single(results, r => r.EntityId == account.Id && r.IdentityId == identity.Id);
        Assert.Equal(0.50m, row.Weight);
    }

    [Fact]
    public async Task MajorityManager_ProposesMostCommonManagerAmongMembers()
    {
        var manager1 = await AddAccountAsync("mgr1");
        var manager2 = await AddAccountAsync("mgr2");
        var identity1 = await AddIdentityAsync("E40", "Manager One", "mgr1owner@corp.com");
        var identity2 = await AddIdentityAsync("E41", "Manager Two", "mgr2owner@corp.com");
        await AddPartyAssignmentAsync("account", manager1.Id, identity1.Id);
        await AddPartyAssignmentAsync("account", manager2.Id, identity2.Id);

        var member1 = await AddAccountAsync("member1");
        var member2 = await AddAccountAsync("member2");
        var member3 = await AddAccountAsync("member3");
        _context.Edges.AddRange(
            OwnershipSeedData.BuildEdge(member1.Id, "account", manager1.Id, "account", "REPORTS_TO"),
            OwnershipSeedData.BuildEdge(member2.Id, "account", manager1.Id, "account", "REPORTS_TO"),
            OwnershipSeedData.BuildEdge(member3.Id, "account", manager2.Id, "account", "REPORTS_TO"));

        var group = OwnershipSeedData.BuildGroup(_sourceId, "eng-team");
        _context.Grps.Add(group);
        await _context.SaveChangesAsync();
        _context.Edges.AddRange(
            OwnershipSeedData.BuildEdge(member1.Id, "account", group.Id, "grp", "MEMBER_OF"),
            OwnershipSeedData.BuildEdge(member2.Id, "account", group.Id, "grp", "MEMBER_OF"),
            OwnershipSeedData.BuildEdge(member3.Id, "account", group.Id, "grp", "MEMBER_OF"));
        await _context.SaveChangesAsync();

        var results = await RunAsync("grp");

        var row = Assert.Single(results, r => r.EntityId == group.Id);
        Assert.Equal(identity1.Id, row.IdentityId); // manager1 manages 2 of 3 members — majority
    }

    [Fact]
    public async Task MajorityGroupOwner_ProposesMostCommonOwnerAcrossGroups()
    {
        var identity1 = await AddIdentityAsync("E50", "Owner One", "owner1@corp.com");
        var identity2 = await AddIdentityAsync("E51", "Owner Two", "owner2@corp.com");

        var group1 = OwnershipSeedData.BuildGroup(_sourceId, "group1");
        var group2 = OwnershipSeedData.BuildGroup(_sourceId, "group2");
        var group3 = OwnershipSeedData.BuildGroup(_sourceId, "group3");
        _context.Grps.AddRange(group1, group2, group3);
        await _context.SaveChangesAsync();

        await AddPartyAssignmentAsync("grp", group1.Id, identity1.Id);
        await AddPartyAssignmentAsync("grp", group2.Id, identity1.Id);
        await AddPartyAssignmentAsync("grp", group3.Id, identity2.Id);

        var account = await AddAccountAsync("multi-group-member");
        _context.Edges.AddRange(
            OwnershipSeedData.BuildEdge(account.Id, "account", group1.Id, "grp", "MEMBER_OF"),
            OwnershipSeedData.BuildEdge(account.Id, "account", group2.Id, "grp", "MEMBER_OF"),
            OwnershipSeedData.BuildEdge(account.Id, "account", group3.Id, "grp", "MEMBER_OF"));
        await _context.SaveChangesAsync();

        var results = await RunAsync("account");

        var row = Assert.Single(results, r => r.EntityId == account.Id);
        Assert.Equal(identity1.Id, row.IdentityId); // owns 2 of the account's 3 groups — majority
    }

    [Fact]
    public async Task CyberArkAccountViaSafe_PolymorphicGroupPrincipal_ResolvesFieldPerType()
    {
        // Seeded inactive by default — activate it just for this test.
        var rule = await _context.OwnershipRules.SingleAsync(r => r.RuleName == OwnershipSeedData.CyberArkAccountViaSafeRule);
        rule.IsActive = true;
        await _context.SaveChangesAsync();

        var privilegedAccount = await AddAccountAsync("priv-account");
        var safe = OwnershipSeedData.BuildAsset(_sourceId, "safe-01");
        _context.Assets.Add(safe);
        await _context.SaveChangesAsync();
        _context.Edges.Add(OwnershipSeedData.BuildEdge(privilegedAccount.Id, "account", safe.Id, "asset", "STORED_IN"));

        // Access comes from a GROUP principal, not an account — exercises the polymorphic
        // (COALESCE-across-types) branch of field resolution after the chain's fan-in hop.
        var accessingGroup = OwnershipSeedData.BuildGroup(_sourceId, "safe-access-group", email: "safeaccess@corp.com");
        _context.Grps.Add(accessingGroup);
        await _context.SaveChangesAsync();
        _context.Edges.Add(OwnershipSeedData.BuildEdge(accessingGroup.Id, "grp", safe.Id, "asset", "HAS_ACCESS"));
        await _context.SaveChangesAsync();

        var identity = await AddIdentityAsync("E60", "Safe Access Owner", "safeaccess@corp.com");

        var results = await RunAsync();

        var row = Assert.Single(results, r => r.EntityId == privilegedAccount.Id);
        Assert.Equal(identity.Id, row.IdentityId);
    }

    private async Task<(Guid ScanId, Guid ManifestId)> SeedScanChainAsync()
    {
        var authMethod = new AuthenticationMethod { Id = Guid.NewGuid(), TypeId = Guid.NewGuid(), Name = "test-auth" };
        _context.AuthenticationMethods.Add(authMethod);
        var scanConfig = new ScanConfig
        {
            Id = Guid.NewGuid(), Name = "test-scan-config", AuthMethodId = authMethod.Id,
            CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow, CreatedBy = "test", UpdatedBy = "test",
        };
        _context.ScanConfigs.Add(scanConfig);
        var scan = new Scan { Id = Guid.NewGuid(), ScanConfigId = scanConfig.Id, ScanType = "initial", Status = "running" };
        _context.Scans.Add(scan);
        var manifest = new ScanManifest
        {
            Id = Guid.NewGuid(), ScanId = scan.Id, FileLocations = [], Status = "pending",
            CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow,
        };
        _context.ScanManifests.Add(manifest);
        await _context.SaveChangesAsync();
        return (scan.Id, manifest.Id);
    }
}

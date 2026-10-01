namespace Thor.Api.Models;

/// <summary>One <c>atre_vote</c> row: account type rule <paramref name="RuleId"/> voted account <paramref name="EntityId"/> into account type <paramref name="VotedFor"/>.</summary>
public sealed record AccountVoteResponse(
    Guid Id,
    Guid EntityId,
    Guid RuleId,
    Guid VotedFor,
    decimal VoteWeight,
    string RunId,
    DateTimeOffset CreatedAt);

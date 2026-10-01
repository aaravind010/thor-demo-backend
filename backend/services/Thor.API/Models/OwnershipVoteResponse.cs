namespace Thor.Api.Models;

/// <summary>One <c>ownership_vote</c> row: ownership rule <paramref name="RuleId"/> voted identity <paramref name="VotedFor"/> as owner of entity <paramref name="EntityId"/>.</summary>
public sealed record OwnershipVoteResponse(
    Guid Id,
    Guid EntityId,
    Guid RuleId,
    Guid VotedFor,
    decimal VoteWeight,
    string RunId,
    DateTimeOffset CreatedAt);

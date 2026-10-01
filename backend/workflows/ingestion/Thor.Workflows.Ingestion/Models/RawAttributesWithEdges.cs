using System.Text.Json.Nodes;

namespace Thor.Workflows.Ingestion.Models;

/// <summary>
/// Raw-attributes shape carried by every connector's accounts/groups/assets. <c>AliasKeys</c>/
/// <c>EdgeRefs</c> stay top-level so <c>Promotion.Promoter</c>/<c>EdgeGate.EdgeResolver</c> can
/// read them without knowing which connector produced the row; every other raw attribute the
/// column map didn't consume is preserved under <c>Extra</c> (see
/// <see cref="AttributeMapping.UnmappedAttributeCollector"/>). An entity with no outbound edges of
/// its own (e.g. a Safe, which is only ever an edge target) just uses an empty <c>EdgeRefs</c> list.
/// </summary>
public sealed record RawAttributesWithEdges(
    IReadOnlyList<string> AliasKeys,
    IReadOnlyList<EdgeRef> EdgeRefs,
    IReadOnlyDictionary<string, JsonNode?> Extra);

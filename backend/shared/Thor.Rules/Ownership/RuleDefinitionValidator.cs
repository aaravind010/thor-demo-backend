using System.Text.Json;

namespace Thor.Rules.Ownership;

/// <summary>
/// Validates a deserialized <see cref="RuleDefinitionPayload"/> against its row's own
/// <c>RuleType</c>/<c>AppliesTo</c> before it's trusted by <c>OwnershipRuleCompiler</c> —
/// a bad rule is rejected here (caller skips + logs it), never allowed to reach SQL generation.
/// Two things this enforces that make the engine safe to run rule content nobody reviewed at
/// compile time: (1) every field name resolves through <see cref="EntityFieldCatalog"/>/
/// <see cref="IdentityFieldCatalog"/> — an unrecognized field fails validation rather than ever
/// becoming a SQL identifier; (2) every hop resolves through <see cref="EdgeTraversalCatalog"/>'s
/// allowlist, and a hop chain that fans into more than one entity type (e.g. <c>HAS_ACCESS</c>
/// from either an account or a group) is only allowed as the last hop — nothing downstream needs
/// to hop further from a polymorphic row set, and disallowing it keeps the compiler simple.
///
/// There is deliberately no phase-order restriction: <c>party_assignment</c> is a durable,
/// cross-run table, so a rule reading a "later" entity type's assignments simply finds nothing on
/// a tenant's very first run and converges over subsequent runs — the same self-healing behavior
/// already accepted for <c>inherit_owner</c>'s multi-level hierarchy walk.
/// </summary>
public static class RuleDefinitionValidator
{
    private const int SupportedSchemaVersion = 1;
    private const int MaxAllowedDepth = 25;
    private const int DefaultMaxDepth = 10;

    private static readonly IReadOnlyDictionary<string, string> SelfReferencingForeignKeys = new Dictionary<string, string>
    {
        [OwnershipEntityTypes.Asset] = "parent_asset_id",
    };

    public static bool TryValidate(string ruleType, string appliesTo, RuleDefinitionPayload payload, out string error)
    {
        if (payload.SchemaVersion != SupportedSchemaVersion)
        {
            return Fail($"unsupported schemaVersion {payload.SchemaVersion}.", out error);
        }

        return ruleType switch
        {
            OwnershipRuleType.FieldMatch => ValidateFieldMatch(appliesTo, payload, out error),
            OwnershipRuleType.SiblingMatch => ValidateSiblingMatch(appliesTo, payload, out error),
            OwnershipRuleType.InheritOwner => ValidateInheritOwner(appliesTo, payload, out error),
            OwnershipRuleType.MajorityOwner => ValidateMajorityOwner(appliesTo, payload, out error),
            _ => Fail($"unrecognized rule_type '{ruleType}'.", out error),
        };
    }

    private static bool Fail(string message, out string error)
    {
        error = message;
        return false;
    }

    // ---------------------------------------------------------------- field_match

    private static bool ValidateFieldMatch(string appliesTo, RuleDefinitionPayload payload, out string error)
    {
        IReadOnlyList<string> currentTypes = [appliesTo];
        if (payload.Via is { Count: > 0 } via)
        {
            if (!ValidateHopChain(via, appliesTo, out currentTypes, out error))
            {
                return false;
            }
        }

        if (!ValidateFieldCondition(currentTypes, payload.ResolvedEntityField, payload.ResolvedIdentityField, payload.How, out error))
        {
            return false;
        }

        if (payload.Cap is { } cap && cap <= 0)
        {
            return Fail("cap must be positive.", out error);
        }

        if (payload.And is { } and)
        {
            foreach (var condition in and)
            {
                if (!ValidateFieldCondition(currentTypes, condition.ResolvedEntityField, condition.ResolvedIdentityField, condition.How, out error))
                {
                    return false;
                }
            }
        }

        error = "";
        return true;
    }

    private static bool ValidateFieldCondition(
        IReadOnlyList<string> currentTypes, string? entityField, string? identityField, string how, out string error)
    {
        if (!RuleOperator.All.Contains(how))
        {
            return Fail($"unrecognized 'how' value '{how}'.", out error);
        }

        if (entityField is null)
        {
            return Fail("missing 'field'/'entityField'.", out error);
        }

        foreach (var type in currentTypes)
        {
            if (EntityFieldCatalog.Resolve(type, entityField, "x") is null)
            {
                return Fail($"unrecognized field '{entityField}' for entity type '{type}'.", out error);
            }
        }

        if (identityField is null || IdentityFieldCatalog.Resolve(identityField, "x") is null)
        {
            return Fail($"unrecognized identity field '{identityField}'.", out error);
        }

        error = "";
        return true;
    }

    // ---------------------------------------------------------------- sibling_match

    private static bool ValidateSiblingMatch(string appliesTo, RuleDefinitionPayload payload, out string error)
    {
        if (payload.Via is { Count: > 0 })
        {
            return Fail("sibling_match does not support 'via' — it always compares against another row of the same entity type.", out error);
        }

        if (!RuleOperator.All.Contains(payload.How))
        {
            return Fail($"unrecognized 'how' value '{payload.How}'.", out error);
        }

        var entityField = payload.ResolvedEntityField;
        var siblingField = payload.ResolvedIdentityField;
        if (entityField is null || siblingField is null)
        {
            return Fail("sibling_match requires 'field' (or both 'entityField' and 'identityField').", out error);
        }

        if (EntityFieldCatalog.Resolve(appliesTo, entityField, "x") is null)
        {
            return Fail($"unrecognized field '{entityField}' for entity type '{appliesTo}'.", out error);
        }

        if (EntityFieldCatalog.Resolve(appliesTo, siblingField, "x") is null)
        {
            return Fail($"unrecognized field '{siblingField}' for entity type '{appliesTo}'.", out error);
        }

        error = "";
        return true;
    }

    // ---------------------------------------------------------------- inherit_owner

    private static bool ValidateInheritOwner(string appliesTo, RuleDefinitionPayload payload, out string error)
    {
        var hasVia = payload.Via is { Count: > 0 };
        var hasWalk = payload.Walk is not null;

        if (hasVia == hasWalk)
        {
            return Fail("inherit_owner requires exactly one of 'via' or 'walk'.", out error);
        }

        if (hasVia)
        {
            if (!ValidateHopChain(payload.Via!, appliesTo, out _, out error))
            {
                return false;
            }

            if (payload.Prefer is { } prefer)
            {
                if (string.IsNullOrEmpty(prefer.Prop))
                {
                    return Fail("prefer.prop is required when prefer is present.", out error);
                }

                // The compiler binds prefer.value via GetBoolean(), which throws on anything else.
                if (prefer.Value.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
                {
                    return Fail("prefer.value must be a boolean.", out error);
                }
            }

            error = "";
            return true;
        }

        return ValidateWalk(appliesTo, payload, out error);
    }

    private static bool ValidateWalk(string appliesTo, RuleDefinitionPayload payload, out string error)
    {
        var walk = payload.Walk!;
        var depth = payload.MaxDepth ?? DefaultMaxDepth;
        if (depth < 1 || depth > MaxAllowedDepth)
        {
            return Fail($"maxDepth must be between 1 and {MaxAllowedDepth}.", out error);
        }

        if (walk.Column is { } column)
        {
            if (!SelfReferencingForeignKeys.TryGetValue(appliesTo, out var allowedColumn) || column != allowedColumn)
            {
                return Fail($"walk.column '{column}' is not an allowlisted self-referencing FK for '{appliesTo}'.", out error);
            }

            error = "";
            return true;
        }

        if (string.IsNullOrEmpty(walk.RelType) || string.IsNullOrEmpty(walk.Direction))
        {
            return Fail("walk requires either 'column' or 'relType'+'direction'.", out error);
        }

        var toType = walk.ToType ?? appliesTo;
        if (toType != appliesTo)
        {
            return Fail($"walk.toType '{toType}' must match the rule's applies_to '{appliesTo}' — a walk only climbs same-type ancestors.", out error);
        }

        if (!EdgeTraversalCatalog.IsAllowed(walk.RelType, walk.Direction, appliesTo, appliesTo))
        {
            return Fail($"walk ({walk.RelType}, {walk.Direction}, {appliesTo}, {appliesTo}) is not an allowlisted self-referential edge.", out error);
        }

        error = "";
        return true;
    }

    // ---------------------------------------------------------------- majority_owner

    private static bool ValidateMajorityOwner(string appliesTo, RuleDefinitionPayload payload, out string error)
    {
        if (payload.Via is not { Count: > 0 } via)
        {
            return Fail("majority_owner requires 'via'.", out error);
        }

        return ValidateHopChain(via, appliesTo, out _, out error);
    }

    // ---------------------------------------------------------------- shared: hop chain

    /// <summary>
    /// Walks a hop chain from <paramref name="startType"/>, validating each hop against
    /// <see cref="EdgeTraversalCatalog"/> and tracking the resulting "current type(s)" — a fan-in
    /// hop (direction "in" with more than one source type) is only legal as the last hop.
    /// </summary>
    private static bool ValidateHopChain(
        IReadOnlyList<EdgeHop> hops, string startType, out IReadOnlyList<string> finalTypes, out string error)
    {
        var current = new List<string> { startType };

        for (var i = 0; i < hops.Count; i++)
        {
            if (current.Count > 1)
            {
                finalTypes = current;
                return Fail("a hop that fans into more than one entity type must be the last hop in the chain.", out error);
            }

            var hop = hops[i];
            var currentType = current[0];

            if (string.IsNullOrEmpty(hop.RelType))
            {
                finalTypes = current;
                return Fail("hop.relType is required.", out error);
            }

            switch (hop.Direction)
            {
                case "out":
                {
                    var fromType = hop.FromType ?? currentType;
                    if (fromType != currentType)
                    {
                        finalTypes = current;
                        return Fail($"hop.fromType '{fromType}' must match the current type '{currentType}'.", out error);
                    }
                    if (hop.ToType is not { } toType)
                    {
                        finalTypes = current;
                        return Fail("hop.toType is required for direction 'out'.", out error);
                    }
                    if (!EdgeTraversalCatalog.IsAllowed(hop.RelType, "out", currentType, toType))
                    {
                        finalTypes = current;
                        return Fail($"hop ({hop.RelType}, out, {currentType}, {toType}) is not an allowlisted edge traversal.", out error);
                    }
                    current = [toType];
                    break;
                }
                case "in":
                {
                    var toType = hop.ToType ?? currentType;
                    if (toType != currentType)
                    {
                        finalTypes = current;
                        return Fail($"hop.toType '{toType}' must match the current type '{currentType}'.", out error);
                    }
                    var fromTypes = hop.ResolvedFromTypes;
                    if (fromTypes.Count == 0)
                    {
                        finalTypes = current;
                        return Fail("hop.fromType/fromTypes is required for direction 'in'.", out error);
                    }
                    foreach (var fromType in fromTypes)
                    {
                        if (!EdgeTraversalCatalog.IsAllowed(hop.RelType, "in", fromType, currentType))
                        {
                            finalTypes = current;
                            return Fail($"hop ({hop.RelType}, in, {fromType}, {currentType}) is not an allowlisted edge traversal.", out error);
                        }
                    }
                    current = fromTypes.ToList();
                    break;
                }
                default:
                    finalTypes = current;
                    return Fail($"hop.direction must be 'out' or 'in', got '{hop.Direction}'.", out error);
            }
        }

        finalTypes = current;
        error = "";
        return true;
    }
}

using System.Text.Json.Nodes;
using Thor.Workflows.Ingestion.Constants;

namespace Thor.Workflows.Ingestion.Models;

/// <summary>
/// AD's clean, documented export contract (§5.1) — the output of <see cref="Extraction.AdExtractor"/>
/// and the input to <see cref="Normalization.AdNormalizer"/>. Stays <see cref="JsonNode"/>-based
/// rather than a fully typed LDAP model: AD's raw attribute shape is dynamic and array-wrapped,
/// and only the normalizer needs to walk it, once.
/// </summary>
/// <param name="RepairedCount">Documents/AccountsPaths entries that were malformed or in a known
/// alternate shape but were successfully recovered.</param>
/// <param name="SkippedCount">Documents/AccountsPaths entries that were unrecognizable or
/// unrecoverable and were dropped.</param>
public sealed record ExtractedAdExport(
    JsonArray Domains,
    short ConnectorType = ConnectorTypes.ActiveDirectory,
    int RepairedCount = 0,
    int SkippedCount = 0);

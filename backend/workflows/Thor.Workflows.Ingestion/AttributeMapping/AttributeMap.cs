namespace Thor.Workflows.Ingestion.AttributeMapping;

/// <summary>
/// Defines, for one connector type + entity kind, which raw connector attribute feeds each
/// staging-table column, and which fields feed the CDC content hash. Keys of
/// <see cref="ColumnMappings"/> are staging-table column names; values are the corresponding
/// raw-attribute names. A raw attribute with no entry here is not dropped — it still flows into
/// the staging row's <c>raw_attributes</c> JSON blob, just not promoted to its own column.
/// </summary>
public sealed record AttributeMap(
    short ConnectorType,
    string EntityKind,
    IReadOnlyDictionary<string, string> ColumnMappings,
    IReadOnlyList<string> HashFields);

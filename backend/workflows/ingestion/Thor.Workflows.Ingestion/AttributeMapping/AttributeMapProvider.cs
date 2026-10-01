using System.Text.Json;
using System.Text.Json.Serialization;

namespace Thor.Workflows.Ingestion.AttributeMapping;

/// <summary>
/// Loads every attribute-map JSON file in a config directory (defaulting to
/// <c>AttributeMapping/Config</c> next to the running assembly) once at construction, keyed by
/// (connector type, entity kind).
/// </summary>
public sealed class AttributeMapProvider : IAttributeMapProvider
{
    private readonly Dictionary<(string Connector, string EntityKind), AttributeMap> _maps;

    public AttributeMapProvider(string? configDirectory = null)
    {
        var dir = configDirectory ?? Path.Combine(AppContext.BaseDirectory, "AttributeMapping", "Config");
        _maps = Directory.EnumerateFiles(dir, "*.json")
            .Select(file => JsonSerializer.Deserialize<AttributeMapDto>(File.ReadAllText(file), JsonOptions)
                ?? throw new InvalidOperationException($"Attribute map file '{file}' did not deserialize."))
            .ToDictionary(
                dto => (dto.Connector, dto.EntityKind),
                dto => new AttributeMap(
                    dto.Connector,
                    dto.EntityKind,
                    dto.ColumnMappings,
                    dto.HashFields));
    }

    public AttributeMap GetMap(string connector, string entityKind) =>
        _maps.TryGetValue((connector, entityKind), out var map)
            ? map
            : throw new InvalidOperationException($"No attribute map configured for connector '{connector}', entity kind '{entityKind}'.");

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private sealed record AttributeMapDto(
        string Connector,
        string EntityKind,
        [property: JsonPropertyName("columnMappings")] IReadOnlyDictionary<string, string> ColumnMappings,
        [property: JsonPropertyName("hashFields")] IReadOnlyList<string> HashFields);
}

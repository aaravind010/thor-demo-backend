namespace Thor.Workflows.Ingestion.AttributeMapping;

public interface IAttributeMapProvider
{
    /// <summary>Throws <see cref="InvalidOperationException"/> if no map is registered for the pair — fail closed rather than silently skipping mapping.</summary>
    AttributeMap GetMap(short connectorType, string entityKind);
}

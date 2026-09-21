namespace Thor.Workflows.Ingestion.Constants;

/// <summary>
/// Numeric connector-type codes shared across every connector's extractor/normalizer. Nothing
/// in the platform reserves specific numbers for specific connectors — these three are just the
/// codes assigned so far.
/// </summary>
public static class ConnectorTypes
{
    public const short ActiveDirectory = 308;
    public const short CyberArk = 401;
    public const short Windows = 402;
}

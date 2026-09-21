namespace Thor.Workflows.IngestionDriver.Core;

/// <summary>The compute targets ingestion can run on, as reported to Step Functions.</summary>
public static class ComputeTarget
{
    public const string Lambda = "lambda";
    public const string EcsTask = "ecs-task";
}

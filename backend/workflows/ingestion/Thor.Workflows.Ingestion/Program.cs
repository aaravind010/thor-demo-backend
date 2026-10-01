using Amazon.S3;
using Microsoft.Extensions.Logging;
using Thor.S3;
using Thor.Workflows.Abstractions;
using Thor.Workflows.Abstractions.Compute;
using Thor.Workflows.Hosting;
using Thor.Workflows.Hosting.Composition;
using Thor.Workflows.Hosting.Compute;
using Thor.Workflows.Ingestion;
using Thor.Workflows.Ingestion.Compute;
using Thor.Workflows.Ingestion.Steps;

using var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole().SetMinimumLevel(LogLevel.Information));
var logger = loggerFactory.CreateLogger("Thor.Workflows.Ingestion");

var steps = new Dictionary<string, Func<IWorkflowStep>>
{
    // Picks lambda vs ecs-task for the rest of the run. A workflow that always wants one target
    // simply omits this step; there is no flag to set.
    [IngestionSteps.SelectCompute] = () => new SelectComputeStep<IngestionRequest>(
        new ScanManifestSizeEstimator(TenantConnectionManagerFactory.Build(), new S3ObjectStore(new AmazonS3Client())),
        SizeThresholdPolicy.FromEnvironment()),
    [IngestionSteps.ExtractStage] = () => new ExtractAndStageStep(),
    [IngestionSteps.Promote] = () => new PromoteStep(),
    [IngestionSteps.GraphLoadStart] = () => new GraphLoadStartStep(),
    [IngestionSteps.GraphLoadPoll] = () => new GraphLoadPollStep(),
};

// One container image, one process entry point, two possible compute targets. Which host to start is
// WorkflowEntryPoint's call, not this module's — every workflow made the same decision the same way.
return await WorkflowEntryPoint.RunAsync(steps, logger);

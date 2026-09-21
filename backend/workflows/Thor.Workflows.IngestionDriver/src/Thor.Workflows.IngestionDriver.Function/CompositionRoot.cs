using Amazon.Lambda.Logging.AspNetCore;
using Amazon.S3;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Thor.S3;
using Thor.Workflows.IngestionDriver.Core;

namespace Thor.Workflows.IngestionDriver.Function;

/// <summary>Wires Core services for the Lambda runtime.</summary>
internal static class CompositionRoot
{
    private const long DefaultDriverMaxBytes = 5 * 1024 * 1024;

    public static IServiceProvider BuildServiceProvider()
    {
        var services = new ServiceCollection();

        services.AddLogging(builder => builder.AddLambdaLogger());

        services.AddSingleton(Thor.Workflows.IngestionDriver.Core.Composition.TenantConnectionManagerFactory.Build());
        services.AddSingleton<IAmazonS3>(_ => new AmazonS3Client());
        services.AddSingleton<IS3ObjectStore, S3ObjectStore>();

        var driverMaxBytes = long.TryParse(Environment.GetEnvironmentVariable("THOR_INGESTION_DRIVER_MAX_BYTES"), out var configuredMaxBytes)
            ? configuredMaxBytes
            : DefaultDriverMaxBytes;
        services.AddSingleton(sp => new ComputeTargetSelector(sp.GetRequiredService<IS3ObjectStore>(), driverMaxBytes));

        services.AddSingleton<IngestionDriverHandler>();

        return services.BuildServiceProvider();
    }
}

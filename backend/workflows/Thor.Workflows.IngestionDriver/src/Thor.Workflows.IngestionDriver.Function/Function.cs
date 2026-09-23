using Amazon.Lambda.Core;
using Amazon.Lambda.Serialization.SystemTextJson;
using Microsoft.Extensions.DependencyInjection;
using Thor.Workflows.IngestionDriver.Core;

[assembly: LambdaSerializer(typeof(DefaultLambdaJsonSerializer))]
[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("Thor.Workflows.IngestionDriver.Test")]

namespace Thor.Workflows.IngestionDriver.Function;

/// <summary>Thin Lambda entry point — all logic lives in Thor.Workflows.IngestionDriver.Core.</summary>
public sealed class Function
{
    private readonly IngestionDriverHandler _handler;

    public Function() : this(CompositionRoot.BuildServiceProvider())
    {
    }

    internal Function(IServiceProvider serviceProvider)
    {
        _handler = serviceProvider.GetRequiredService<IngestionDriverHandler>();
    }

    public Task<ComputeSelectionResult> FunctionHandler(IngestionDriverRequest request, ILambdaContext context) =>
        _handler.HandleAsync(request);
}

using System.Text.Json.Serialization;
using Amazon.Lambda.Core;
using Amazon.S3.Util;
using Microsoft.Extensions.DependencyInjection;
using Thor.CreateManifest.Core;

[assembly: LambdaSerializer(typeof(Amazon.Lambda.Serialization.SystemTextJson.DefaultLambdaJsonSerializer))]
[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("Thor.CreateManifest.Test")]

namespace Thor.CreateManifest.Function;

/// <summary>The fields this Lambda needs from an EventBridge Pipe's raw SQS message envelope.</summary>
public sealed record SqsPipeMessage(
    [property: JsonPropertyName("messageId")] string MessageId,
    [property: JsonPropertyName("body")] string Body);

/// <summary>
/// Lambda entry point. Extracts <see cref="UploadEvent"/> bucket/key pairs from a batch of
/// S3 event notifications (delivered via an EventBridge Pipe as a JSON array of
/// <see cref="SqsPipeMessage"/>) and hands them to <see cref="ManifestBatchProcessor"/>.
/// </summary>
public sealed class Function
{
    private readonly ManifestBatchProcessor _processor;

    public Function() : this(CompositionRoot.BuildServiceProvider())
    {
    }

    internal Function(IServiceProvider serviceProvider)
    {
        _processor = serviceProvider.GetRequiredService<ManifestBatchProcessor>();
    }

    public async Task FunctionHandler(List<SqsPipeMessage> records, ILambdaContext context)
    {
        var uploadEvents = new List<UploadEvent>();
        foreach (var record in records)
        {
            S3EventNotification notification;
            try
            {
                notification = S3EventNotification.ParseJson(record.Body);
            }
            catch (Exception ex)
            {
                context.Logger.LogWarning($"Skipping record {record.MessageId} — body is not a parseable S3 event notification: {ex.Message}");
                continue;
            }

            foreach (var s3Record in notification.Records ?? [])
            {
                // S3 event notifications URL-encode the object key (e.g. spaces as '+').
                var key = Uri.UnescapeDataString(s3Record.S3.Object.Key.Replace("+", " "));
                uploadEvents.Add(new UploadEvent(s3Record.S3.Bucket.Name, key));
            }
        }

        await _processor.ProcessBatchAsync(uploadEvents);
    }
}

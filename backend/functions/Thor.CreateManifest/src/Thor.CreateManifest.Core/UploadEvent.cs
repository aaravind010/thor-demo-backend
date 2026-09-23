namespace Thor.CreateManifest.Core;

/// <summary>One S3 upload event from the batch this Lambda's trigger hands it — trigger-agnostic, built by Function.cs.</summary>
public sealed record UploadEvent(string Bucket, string Key);

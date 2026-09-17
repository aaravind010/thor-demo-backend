using Amazon.S3;
using Amazon.S3.Model;

namespace Thor.S3;

public sealed class S3ObjectStore(IAmazonS3 s3Client) : IS3ObjectStore
{
    public async Task<IReadOnlyList<string>> ListKeysAsync(string bucket, string prefix)
    {
        var keys = new List<string>();
        string? continuationToken = null;
        do
        {
            var response = await s3Client.ListObjectsV2Async(new ListObjectsV2Request
            {
                BucketName = bucket,
                Prefix = prefix,
                ContinuationToken = continuationToken,
            });

            keys.AddRange(response.S3Objects.Select(o => o.Key));

            continuationToken = response.IsTruncated == true ? response.NextContinuationToken : null;
        } while (continuationToken is not null);

        return keys;
    }

    public async Task<byte[]> GetObjectAsync(string bucket, string key)
    {
        using var response = await s3Client.GetObjectAsync(bucket, key);
        using var buffer = new MemoryStream();
        await response.ResponseStream.CopyToAsync(buffer);
        return buffer.ToArray();
    }

    public async Task PutObjectAsync(string bucket, string key, byte[] content)
    {
        using var stream = new MemoryStream(content);
        await s3Client.PutObjectAsync(new PutObjectRequest
        {
            BucketName = bucket,
            Key = key,
            InputStream = stream,
            AutoCloseStream = false,
        });
    }
}

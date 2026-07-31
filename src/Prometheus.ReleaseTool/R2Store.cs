using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using Prometheus.Update;
using System.Net;

namespace Prometheus.ReleaseTool;

internal sealed class R2Store : IDisposable
{
    private readonly IAmazonS3 _client;
    private readonly string _bucket;

    public R2Store(ReleaseOptions options)
    {
        _bucket = options.Bucket;
        var config = new AmazonS3Config
        {
            ServiceURL = $"https://{options.AccountId}.r2.cloudflarestorage.com",
            AuthenticationRegion = "auto",
            ForcePathStyle = true
        };
        _client = new AmazonS3Client(
            new BasicAWSCredentials(options.AccessKey, options.SecretKey), config);
    }

    public async Task<byte[]?> TryGetAsync(string objectKey,
        CancellationToken cancellationToken = default)
    {
        try
        {
            using var response = await _client.GetObjectAsync(_bucket, objectKey,
                cancellationToken).ConfigureAwait(false);
            await using var memory = new MemoryStream();
            await response.ResponseStream.CopyToAsync(memory, cancellationToken)
                .ConfigureAwait(false);
            return memory.ToArray();
        }
        catch (AmazonS3Exception exception) when (exception.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    public async Task EnsureMissingAsync(string objectKey,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await _client.GetObjectMetadataAsync(_bucket, objectKey, cancellationToken)
                .ConfigureAwait(false);
            throw new InvalidOperationException(
                $"Immutable R2 release object already exists: {objectKey}");
        }
        catch (AmazonS3Exception exception) when (exception.StatusCode == HttpStatusCode.NotFound)
        {
        }
    }

    public async Task PutFileAsync(UploadMapEntry entry,
        CancellationToken cancellationToken = default)
    {
        var request = new PutObjectRequest
        {
            BucketName = _bucket,
            Key = entry.ObjectKey,
            FilePath = entry.LocalPath,
            ContentType = entry.ContentType,
            DisablePayloadSigning = true
        };
        await _client.PutObjectAsync(request, cancellationToken).ConfigureAwait(false);
    }

    public void Dispose()
    {
        _client.Dispose();
    }
}

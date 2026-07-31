using System.Security.Cryptography;
using System.Text.Json;
using Prometheus.Update;

namespace Prometheus.ReleaseTool;

internal static class ReleasePublisher
{
    private const string ChannelObjectKey = "channels/stable/win-x64.json";

    public static async Task PublishAsync(ReleaseOptions options,
        CancellationToken cancellationToken = default)
    {
        using var signingKey = ECDsa.Create();
        signingKey.ImportFromPem(await File.ReadAllTextAsync(options.PrivateKeyPath,
            cancellationToken).ConfigureAwait(false));
        if (signingKey.KeySize != 256)
        {
            throw new CryptographicException("The update signing key must use ECDSA P-256.");
        }
        using var store = new R2Store(options);
        var channel = await LoadChannelAsync(store, signingKey, cancellationToken)
            .ConfigureAwait(false);
        UpdateValidation.ValidateChannelIndex(channel, allowEmpty: true);
        if (channel.Releases.Any(release =>
                string.Equals(release.Version, options.Version, StringComparison.Ordinal)))
        {
            throw new InvalidOperationException(
                $"Release {options.Version} already exists in the stable channel.");
        }
        if (channel.Releases.Count > 0
            && UpdateVersion.Parse(options.Version).CompareTo(
                UpdateVersion.Parse(channel.Releases[0].Version)) <= 0)
        {
            throw new InvalidOperationException(
                "A stable release must be newer than the current channel release.");
        }

        var previousManifests = new List<SignedEnvelope>();
        foreach (var release in channel.Releases.Take(3))
        {
            var bytes = await store.TryGetAsync(release.ManifestObjectKey, cancellationToken)
                .ConfigureAwait(false)
                ?? throw new InvalidDataException(
                    $"Previous manifest is missing: {release.ManifestObjectKey}");
            previousManifests.Add(JsonSerializer.Deserialize(bytes,
                                      UpdateJsonContext.Default.SignedEnvelope)
                                  ?? throw new InvalidDataException(
                                      "A previous manifest envelope is empty."));
        }

        var build = await ReleaseBuilder.BuildAsync(options, signingKey, previousManifests,
            cancellationToken).ConfigureAwait(false);
        foreach (var upload in build.Uploads)
        {
            await store.EnsureMissingAsync(upload.ObjectKey, cancellationToken)
                .ConfigureAwait(false);
        }
        foreach (var upload in build.Uploads)
        {
            Console.WriteLine($"Uploading {upload.ObjectKey}");
            await store.PutFileAsync(upload, cancellationToken).ConfigureAwait(false);
        }

        var prefix = $"releases/{options.Version}/{UpdateProtocol.WindowsX64Rid}";
        channel.Releases.Insert(0, new ChannelRelease
        {
            Version = options.Version,
            ReleaseObjectKey = $"{prefix}/release.json",
            ManifestObjectKey = $"{prefix}/manifest.json",
            PublishedAt = build.Descriptor.PublishedAt
        });
        if (channel.Releases.Count > 20)
        {
            channel.Releases.RemoveRange(20, channel.Releases.Count - 20);
        }
        UpdateValidation.ValidateChannelIndex(channel);

        var channelEnvelope = UpdateSecurity.Sign(channel, signingKey,
            UpdateJsonContext.Default.ChannelIndex);
        var channelPath = Path.Combine(options.OutputDirectory, "channel.json");
        File.WriteAllBytes(channelPath, JsonSerializer.SerializeToUtf8Bytes(channelEnvelope,
            UpdateJsonContext.Default.SignedEnvelope));
        Console.WriteLine($"Publishing channel pointer {ChannelObjectKey}");
        await store.PutFileAsync(new UploadMapEntry
        {
            LocalPath = channelPath,
            ObjectKey = ChannelObjectKey,
            ContentType = "application/json"
        }, cancellationToken).ConfigureAwait(false);

        var uploadMap = new UploadMap
        {
            Files = build.Uploads,
            ChannelPointer = new UploadMapEntry
            {
                LocalPath = channelPath,
                ObjectKey = ChannelObjectKey,
                ContentType = "application/json"
            }
        };
        File.WriteAllBytes(Path.Combine(options.OutputDirectory, "upload-map.json"),
            JsonSerializer.SerializeToUtf8Bytes(uploadMap, UpdateJsonContext.Default.UploadMap));
    }

    private static async Task<ChannelIndex> LoadChannelAsync(R2Store store, ECDsa signingKey,
        CancellationToken cancellationToken)
    {
        var bytes = await store.TryGetAsync(ChannelObjectKey, cancellationToken)
            .ConfigureAwait(false);
        if (bytes is null)
        {
            return new ChannelIndex();
        }

        var envelope = JsonSerializer.Deserialize(bytes, UpdateJsonContext.Default.SignedEnvelope)
            ?? throw new InvalidDataException("The stable channel pointer is empty.");
        using var publicKey = ECDsa.Create();
        publicKey.ImportSubjectPublicKeyInfo(signingKey.ExportSubjectPublicKeyInfo(), out _);
        var channel = UpdateSecurity.VerifyAndDeserialize(envelope, publicKey,
            UpdateJsonContext.Default.ChannelIndex);
        UpdateValidation.ValidateChannelIndex(channel, allowEmpty: true);
        return channel;
    }
}

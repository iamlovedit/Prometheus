using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.IO.Compression;
using Prometheus.Services.Interfaces.Updates;
using Prometheus.Services.Updates;
using Prometheus.Update;

namespace Prometheus.Update.Tests;

public sealed class UpdateServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(),
        "prometheus-update-service-tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task CheckAsync_WhenApiReturnsNoContent_SetsUpToDate()
    {
        using var service = CreateService(_ => new HttpResponseMessage(HttpStatusCode.NoContent));

        var update = await service.CheckAsync(true);

        Assert.Null(update);
        Assert.Equal(UpdateState.UpToDate, service.State);
    }

    [Fact]
    public async Task CheckAsync_WhenCurrentVersionIsBelowMinimum_ReturnsMandatoryUpdate()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var descriptor = CreateDescriptor();
        var envelope = UpdateSecurity.Sign(descriptor, key,
            UpdateJsonContext.Default.ReleaseDescriptor);
        var responseBody = new UpdateApiResponse
        {
            Release = envelope,
            SelectedArtifactId = "full",
            ManifestUrl = new Uri("https://r2.example/manifest"),
            PackageUrl = new Uri("https://r2.example/full"),
            FullPackageUrl = new Uri("https://r2.example/full"),
            ExpiresAt = DateTimeOffset.UtcNow.AddHours(6)
        };
        using var publicKey = ECDsa.Create();
        publicKey.ImportSubjectPublicKeyInfo(key.ExportSubjectPublicKeyInfo(), out _);
        using var service = CreateService(_ => JsonResponse(responseBody), envelopeValue =>
            UpdateSecurity.VerifyAndDeserialize(envelopeValue, publicKey,
                UpdateJsonContext.Default.ReleaseDescriptor));

        var update = await service.CheckAsync(true);

        Assert.NotNull(update);
        Assert.True(update.IsMandatory);
        Assert.Equal("2.0.0", update.Descriptor.Version);
        Assert.Equal(UpdateState.Available, service.State);
    }

    [Fact]
    public async Task CheckAsync_WhenAutomaticRequestFails_ReturnsToIdleWithoutUserError()
    {
        using var service = CreateService(_ => new HttpResponseMessage(
            HttpStatusCode.ServiceUnavailable));

        var update = await service.CheckAsync(false);

        Assert.Null(update);
        Assert.Equal(UpdateState.Idle, service.State);
        Assert.Null(service.ErrorMessage);
    }

    [Fact]
    public async Task CheckAsync_WhenManualRequestFails_ExposesError()
    {
        using var service = CreateService(_ => new HttpResponseMessage(
            HttpStatusCode.ServiceUnavailable));

        var update = await service.CheckAsync(true);

        Assert.Null(update);
        Assert.Equal(UpdateState.Failed, service.State);
        Assert.False(string.IsNullOrWhiteSpace(service.ErrorMessage));
    }

    [Fact]
    public async Task CheckAsync_WhenCancelled_ReturnsToIdle()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var service = CreateService(new AsyncStubHandler(async (_, cancellationToken) =>
        {
            started.SetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.NoContent);
        }));
        using var cancellation = new CancellationTokenSource();

        var check = service.CheckAsync(true, cancellation.Token);
        await started.Task;
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => check);
        Assert.Equal(UpdateState.Idle, service.State);
    }

    [Fact]
    public async Task CheckAsync_WhenAnotherCheckIsRunning_DoesNotStartASecondRequest()
    {
        var requestCount = 0;
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var service = CreateService(new AsyncStubHandler(async (_, cancellationToken) =>
        {
            Interlocked.Increment(ref requestCount);
            started.SetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.NoContent);
        }));
        using var cancellation = new CancellationTokenSource();

        var first = service.CheckAsync(true, cancellation.Token);
        await started.Task;
        var second = await service.CheckAsync(true);
        cancellation.Cancel();

        Assert.Null(second);
        Assert.Equal(1, requestCount);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => first);
    }

    [Fact]
    public async Task DownloadAsync_WhenInstalledBaseIsMissing_UsesFullPackageFallback()
    {
        var fixture = CreateDownloadFixture(includeDelta: true);
        var fullRequested = false;
        using var service = CreateService(request =>
        {
            if (request.RequestUri!.AbsolutePath.EndsWith("/api/v1/updates/windows"))
            {
                return JsonResponse(fixture.Response);
            }
            if (request.RequestUri.AbsolutePath.EndsWith("/manifest"))
            {
                return BytesResponse(fixture.ManifestBytes);
            }
            if (request.RequestUri.AbsolutePath.EndsWith("/full"))
            {
                fullRequested = true;
                return BytesResponse(fixture.FullPackageBytes);
            }
            throw new InvalidOperationException($"Unexpected request {request.RequestUri}");
        }, fixture.Verify, fixture.VerifyManifest);

        await service.CheckAsync(true);
        await service.DownloadAsync();

        Assert.True(fullRequested);
        Assert.Equal(UpdateState.ReadyToInstall, service.State);
    }

    [Fact]
    public async Task DownloadAsync_WhenUrlExpires_RefreshesAndResumesWithRange()
    {
        var fixture = CreateDownloadFixture(includeDelta: false);
        var refreshedResponse = CloneResponse(fixture.Response,
            new Uri("https://r2.example/fresh-full"));
        var apiRequests = 0;
        var rangeStart = fixture.FullPackageBytes.Length / 2;
        var partialPath = Path.Combine(_root, "data", "Updates", "2.0.0",
            "full.zip.part");
        Directory.CreateDirectory(Path.GetDirectoryName(partialPath)!);
        await File.WriteAllBytesAsync(partialPath,
            fixture.FullPackageBytes.AsMemory(0, rangeStart).ToArray());
        using var service = CreateService(request =>
        {
            if (request.RequestUri!.AbsolutePath.EndsWith("/api/v1/updates/windows"))
            {
                apiRequests++;
                return JsonResponse(apiRequests == 1 ? fixture.Response : refreshedResponse);
            }
            if (request.RequestUri.AbsolutePath.EndsWith("/manifest"))
            {
                return BytesResponse(fixture.ManifestBytes);
            }
            if (request.RequestUri.AbsolutePath.EndsWith("/full"))
            {
                return new HttpResponseMessage(HttpStatusCode.Forbidden);
            }
            if (request.RequestUri.AbsolutePath.EndsWith("/fresh-full"))
            {
                Assert.Equal(rangeStart, request.Headers.Range?.Ranges.Single().From);
                var response = BytesResponse(fixture.FullPackageBytes[rangeStart..],
                    HttpStatusCode.PartialContent);
                response.Content.Headers.ContentRange = new ContentRangeHeaderValue(rangeStart,
                    fixture.FullPackageBytes.Length - 1, fixture.FullPackageBytes.Length);
                return response;
            }
            throw new InvalidOperationException($"Unexpected request {request.RequestUri}");
        }, fixture.Verify, fixture.VerifyManifest);

        await service.CheckAsync(true);
        await service.DownloadAsync();

        Assert.Equal(2, apiRequests);
        Assert.Equal(UpdateState.ReadyToInstall, service.State);
    }

    [Fact]
    public async Task DownloadAsync_WhenRefreshedObjectChanges_RejectsReplacement()
    {
        var fixture = CreateDownloadFixture(includeDelta: false);
        using var replacementKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var replacementDescriptor = CreateDescriptor();
        replacementDescriptor.TargetManifest = fixture.Descriptor.TargetManifest;
        replacementDescriptor.FullPackage = Artifact("full", "full.zip",
            fixture.FullPackageBytes.Length, 'e');
        var replacementEnvelope = UpdateSecurity.Sign(replacementDescriptor, replacementKey,
            UpdateJsonContext.Default.ReleaseDescriptor);
        var replacementResponse = CloneResponse(fixture.Response,
            new Uri("https://r2.example/replacement"));
        replacementResponse.Release = replacementEnvelope;
        using var replacementPublicKey = ECDsa.Create();
        replacementPublicKey.ImportSubjectPublicKeyInfo(
            replacementKey.ExportSubjectPublicKeyInfo(), out _);
        var apiRequests = 0;
        using var service = CreateService(request =>
        {
            if (request.RequestUri!.AbsolutePath.EndsWith("/api/v1/updates/windows"))
            {
                apiRequests++;
                return JsonResponse(apiRequests == 1 ? fixture.Response : replacementResponse);
            }
            if (request.RequestUri.AbsolutePath.EndsWith("/manifest"))
            {
                return BytesResponse(fixture.ManifestBytes);
            }
            if (request.RequestUri.AbsolutePath.EndsWith("/full"))
            {
                return new HttpResponseMessage(HttpStatusCode.Forbidden);
            }
            throw new InvalidOperationException($"Unexpected request {request.RequestUri}");
        }, envelope => string.Equals(envelope.Payload, fixture.Response.Release.Payload,
            StringComparison.Ordinal)
            ? fixture.Verify(envelope)
            : UpdateSecurity.VerifyAndDeserialize(envelope, replacementPublicKey,
                UpdateJsonContext.Default.ReleaseDescriptor), fixture.VerifyManifest);

        await service.CheckAsync(true);
        await Assert.ThrowsAsync<InvalidDataException>(() => service.DownloadAsync());

        Assert.Equal(UpdateState.Failed, service.State);
    }

    private UpdateService CreateService(Func<HttpRequestMessage, HttpResponseMessage> response,
        Func<SignedEnvelope, ReleaseDescriptor>? verifier = null,
        Func<SignedEnvelope, ReleaseManifest>? manifestVerifier = null)
    {
        return CreateService(new StubHandler(response), verifier, manifestVerifier);
    }

    private UpdateService CreateService(HttpMessageHandler handler,
        Func<SignedEnvelope, ReleaseDescriptor>? verifier = null,
        Func<SignedEnvelope, ReleaseManifest>? manifestVerifier = null)
    {
        Directory.CreateDirectory(_root);
        var client = new HttpClient(handler);
        return new UpdateService(new UpdateServiceOptions
        {
            ApiBaseUrl = "https://updates.example/",
            CurrentVersion = "1.0.0",
            BootstrapperVersion = "1.0.0",
            InstallRoot = _root,
            LocalDataRoot = Path.Combine(_root, "data")
        }, client, verifier ?? (_ => CreateDescriptor()), manifestVerifier);
    }

    private static ReleaseDescriptor CreateDescriptor()
    {
        return new ReleaseDescriptor
        {
            Version = "2.0.0",
            MinimumSupportedVersion = "1.1.0",
            MinimumBootstrapperVersion = "1.0.0",
            BootstrapperVersion = "2.0.0",
            PublishedAt = DateTimeOffset.UtcNow,
            TargetManifest = Artifact("manifest", "manifest.json", 10, 'a'),
            FullPackage = Artifact("full", "full.zip", 100, 'b'),
            PortablePackage = Artifact("portable", "portable.zip", 120, 'c')
        };
    }

    private DownloadFixture CreateDownloadFixture(bool includeDelta)
    {
        var desktopBytes = new byte[] { 42 };
        var fullPackageBytes = CreateZip(UpdateProtocol.DesktopExecutableName, desktopBytes);
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var manifest = new ReleaseManifest
        {
            Version = "2.0.0",
            Files =
            [
                new ReleaseFileEntry
                {
                    Path = UpdateProtocol.DesktopExecutableName,
                    Size = desktopBytes.Length,
                    Sha256 = Hash(desktopBytes)
                }
            ]
        };
        var manifestEnvelope = UpdateSecurity.Sign(manifest, key,
            UpdateJsonContext.Default.ReleaseManifest);
        var manifestBytes = JsonSerializer.SerializeToUtf8Bytes(manifestEnvelope,
            UpdateJsonContext.Default.SignedEnvelope);
        var descriptor = CreateDescriptor();
        descriptor.TargetManifest = Artifact("manifest", "manifest.json",
            manifestBytes.Length, Hash(manifestBytes));
        descriptor.FullPackage = Artifact("full", "full.zip", fullPackageBytes.Length,
            Hash(fullPackageBytes));
        if (includeDelta)
        {
            descriptor.Deltas.Add(new DeltaArtifact
            {
                Id = "delta:1.0.0",
                BaseVersion = "1.0.0",
                ObjectKey = "releases/2.0.0/win-x64/deltas/from-1.0.0.zip",
                Size = 10,
                Sha256 = new string('d', 64)
            });
        }
        var releaseEnvelope = UpdateSecurity.Sign(descriptor, key,
            UpdateJsonContext.Default.ReleaseDescriptor);
        using var publicKey = ECDsa.Create();
        publicKey.ImportSubjectPublicKeyInfo(key.ExportSubjectPublicKeyInfo(), out _);
        var publicKeyBytes = publicKey.ExportSubjectPublicKeyInfo();
        ReleaseDescriptor Verify(SignedEnvelope envelope)
        {
            using var verifier = ECDsa.Create();
            verifier.ImportSubjectPublicKeyInfo(publicKeyBytes, out _);
            return UpdateSecurity.VerifyAndDeserialize(envelope, verifier,
                UpdateJsonContext.Default.ReleaseDescriptor);
        }
        ReleaseManifest VerifyManifest(SignedEnvelope envelope)
        {
            using var verifier = ECDsa.Create();
            verifier.ImportSubjectPublicKeyInfo(publicKeyBytes, out _);
            return UpdateSecurity.VerifyAndDeserialize(envelope, verifier,
                UpdateJsonContext.Default.ReleaseManifest);
        }
        return new DownloadFixture(descriptor, new UpdateApiResponse
        {
            Release = releaseEnvelope,
            SelectedArtifactId = includeDelta ? "delta:1.0.0" : "full",
            ManifestUrl = new Uri("https://r2.example/manifest"),
            PackageUrl = new Uri(includeDelta
                ? "https://r2.example/delta" : "https://r2.example/full"),
            FullPackageUrl = new Uri("https://r2.example/full"),
            ExpiresAt = DateTimeOffset.UtcNow.AddHours(6)
        }, manifestBytes, fullPackageBytes, Verify, VerifyManifest);
    }

    private static UpdateApiResponse CloneResponse(UpdateApiResponse source, Uri packageUrl)
    {
        return new UpdateApiResponse
        {
            Release = source.Release,
            SelectedArtifactId = source.SelectedArtifactId,
            ManifestUrl = source.ManifestUrl,
            PackageUrl = packageUrl,
            FullPackageUrl = packageUrl,
            BootstrapperUrl = source.BootstrapperUrl,
            ExpiresAt = DateTimeOffset.UtcNow.AddHours(6)
        };
    }

    private static byte[] CreateZip(string path, byte[] bytes)
    {
        using var memory = new MemoryStream();
        using (var archive = new ZipArchive(memory, ZipArchiveMode.Create, true))
        {
            var entry = archive.CreateEntry(path);
            using var stream = entry.Open();
            stream.Write(bytes);
        }
        return memory.ToArray();
    }

    private static string Hash(byte[] bytes)
    {
        return Convert.ToHexStringLower(SHA256.HashData(bytes));
    }

    private static UpdateArtifact Artifact(string id, string key, long size, char hash)
    {
        return new UpdateArtifact
        {
            Id = id,
            ObjectKey = $"releases/2.0.0/win-x64/{key}",
            Size = size,
            Sha256 = new string(hash, 64)
        };
    }

    private static UpdateArtifact Artifact(string id, string key, long size, string hash)
    {
        return new UpdateArtifact
        {
            Id = id,
            ObjectKey = $"releases/2.0.0/win-x64/{key}",
            Size = size,
            Sha256 = hash
        };
    }

    private static HttpResponseMessage JsonResponse(UpdateApiResponse value)
    {
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(JsonSerializer.Serialize(value,
                UpdateJsonContext.Default.UpdateApiResponse), Encoding.UTF8, "application/json")
        };
    }

    private static HttpResponseMessage BytesResponse(byte[] value,
        HttpStatusCode statusCode = HttpStatusCode.OK)
    {
        return new HttpResponseMessage(statusCode)
        {
            Content = new ByteArrayContent(value)
        };
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, true);
        }
    }

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> response)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(response(request));
        }
    }

    private sealed class AsyncStubHandler(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> response)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            return response(request, cancellationToken);
        }
    }

    private sealed record DownloadFixture(ReleaseDescriptor Descriptor,
        UpdateApiResponse Response, byte[] ManifestBytes, byte[] FullPackageBytes,
        Func<SignedEnvelope, ReleaseDescriptor> Verify,
        Func<SignedEnvelope, ReleaseManifest> VerifyManifest);
}

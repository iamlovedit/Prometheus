using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Prometheus.Services.Interfaces.Updates;
using Prometheus.Services.Updates;
using Prometheus.Update;

namespace Prometheus.Update.Tests;

public sealed class UpdateServiceTests : IDisposable
{
    private const string Owner = "iamlovedit";
    private const string Repository = "Prometheus";
    private readonly string _root = Path.Combine(Path.GetTempPath(),
        "prometheus-update-service-tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task CheckAsync_WithNewStableRelease_ReturnsAvailableUpdateAndGitHubHeaders()
    {
        var fixture = CreateFixture("2.0.0");
        string? requestedUri = null;
        string? accept = null;
        string? apiVersion = null;
        using var service = CreateService(request =>
        {
            requestedUri = request.RequestUri?.ToString();
            accept = string.Join(',', request.Headers.Accept.Select(value => value.MediaType));
            apiVersion = request.Headers.GetValues("X-GitHub-Api-Version").Single();
            return JsonResponse(fixture.Release);
        });

        var update = await service.CheckAsync(true);

        Assert.NotNull(update);
        Assert.Equal("2.0.0", update.Version);
        Assert.Equal("Release notes", update.ReleaseNotes);
        Assert.False(update.IsMandatory);
        Assert.Equal(UpdateState.Available, service.State);
        Assert.Equal("https://api.github.com/repos/iamlovedit/Prometheus/releases/latest",
            requestedUri);
        Assert.Contains(UpdateProtocol.GitHubApiAccept, accept);
        Assert.Equal(UpdateProtocol.GitHubApiVersion, apiVersion);
    }

    [Theory]
    [InlineData("1.0.0")]
    [InlineData("0.9.9")]
    public async Task CheckAsync_WhenReleaseIsNotNewer_ReturnsUpToDate(string version)
    {
        var fixture = CreateFixture(version);
        using var service = CreateService(_ => JsonResponse(fixture.Release));

        var update = await service.CheckAsync(true);

        Assert.Null(update);
        Assert.Null(service.AvailableUpdate);
        Assert.Equal(UpdateState.UpToDate, service.State);
    }

    [Fact]
    public async Task CheckAsync_WithDraftRelease_ReportsFailureInsteadOfUpToDate()
    {
        var fixture = CreateFixture("2.0.0");
        fixture.Release.Draft = true;
        using var service = CreateService(_ => JsonResponse(fixture.Release));

        var update = await service.CheckAsync(true);

        Assert.Null(update);
        Assert.Equal(UpdateState.Failed, service.State);
        Assert.NotNull(service.ErrorMessage);
    }

    [Fact]
    public async Task CheckAsync_WhenAutomaticCheckIsRateLimited_RemainsNonBlocking()
    {
        using var service = CreateService(_ => new HttpResponseMessage(HttpStatusCode.Forbidden));

        var update = await service.CheckAsync(false);

        Assert.Null(update);
        Assert.Equal(UpdateState.Idle, service.State);
        Assert.Null(service.ErrorMessage);
    }

    [Fact]
    public async Task CheckAsync_WithEtag_ReusesCachedReleaseOnNotModified()
    {
        var fixture = CreateFixture("2.0.0");
        var calls = 0;
        using var service = CreateService(request =>
        {
            calls++;
            if (calls == 1)
            {
                var response = JsonResponse(fixture.Release);
                response.Headers.ETag = new EntityTagHeaderValue("\"release-2\"");
                return response;
            }

            Assert.Contains(request.Headers.IfNoneMatch,
                value => value.Tag == "\"release-2\"");
            return new HttpResponseMessage(HttpStatusCode.NotModified);
        });

        var first = await service.CheckAsync(true);
        var second = await service.CheckAsync(true);

        Assert.Equal("2.0.0", first?.Version);
        Assert.Equal("2.0.0", second?.Version);
        Assert.Equal(2, calls);
    }

    [Fact]
    public async Task DownloadAsync_WithValidAssets_VerifiesAndPreparesPackage()
    {
        var fixture = CreateFixture("2.0.0", [1, 2, 3, 4, 5]);
        using var service = CreateService(request => RouteFixture(request, fixture));
        await service.CheckAsync(true);

        await service.DownloadAsync();

        var packagePath = Path.Combine(_root, "local", "Updates", "2.0.0",
            fixture.PackageName);
        Assert.Equal(UpdateState.ReadyToInstall, service.State);
        Assert.Equal(fixture.PackageBytes, await File.ReadAllBytesAsync(packagePath));
        Assert.False(File.Exists(packagePath + ".part"));
    }

    [Fact]
    public async Task DownloadAsync_WithMatchingPartialFile_UsesHttpRange()
    {
        var fixture = CreateFixture("2.0.0", [1, 2, 3, 4, 5, 6, 7, 8]);
        using var service = CreateService(request =>
        {
            if (request.RequestUri!.AbsolutePath.EndsWith("/releases/latest",
                    StringComparison.Ordinal))
            {
                return JsonResponse(fixture.Release);
            }
            if (request.RequestUri.AbsolutePath.EndsWith(".sha256",
                    StringComparison.Ordinal))
            {
                return BytesResponse(fixture.ChecksumBytes);
            }

            Assert.Equal(3, request.Headers.Range?.Ranges.Single().From);
            var remaining = fixture.PackageBytes[3..];
            var response = BytesResponse(remaining, HttpStatusCode.PartialContent);
            response.Content.Headers.ContentRange = new ContentRangeHeaderValue(3,
                fixture.PackageBytes.Length - 1, fixture.PackageBytes.Length);
            return response;
        });
        await service.CheckAsync(true);
        var updateRoot = Path.Combine(_root, "local", "Updates", "2.0.0");
        Directory.CreateDirectory(updateRoot);
        var packagePath = Path.Combine(updateRoot, fixture.PackageName);
        await File.WriteAllBytesAsync(packagePath + ".part", fixture.PackageBytes[..3]);
        UpdatePaths.WriteJsonAtomic(Path.Combine(updateRoot, "download.json"),
            new UpdateDownloadMetadata
            {
                Version = "2.0.0",
                AssetName = fixture.PackageName,
                AssetSize = fixture.PackageBytes.Length
            }, UpdateJsonContext.Default.UpdateDownloadMetadata);

        await service.DownloadAsync();

        Assert.Equal(fixture.PackageBytes, await File.ReadAllBytesAsync(packagePath));
        Assert.Equal(UpdateState.ReadyToInstall, service.State);
    }

    [Fact]
    public async Task DownloadAsync_WhenSha256DoesNotMatch_DeletesUntrustedPackage()
    {
        var fixture = CreateFixture("2.0.0", [1, 2, 3]);
        fixture.ChecksumBytes = Encoding.ASCII.GetBytes(
            $"{new string('0', 64)}  {fixture.PackageName}");
        fixture.Release.Assets.Single(asset => asset.Name.EndsWith(".sha256",
            StringComparison.Ordinal)).Size = fixture.ChecksumBytes.Length;
        using var service = CreateService(request => RouteFixture(request, fixture));
        await service.CheckAsync(true);

        await Assert.ThrowsAsync<CryptographicException>(() => service.DownloadAsync());

        var packagePath = Path.Combine(_root, "local", "Updates", "2.0.0",
            fixture.PackageName);
        Assert.Equal(UpdateState.Failed, service.State);
        Assert.False(File.Exists(packagePath));
    }

    [Fact]
    public async Task DownloadAsync_WhenCancelled_ReturnsToAvailableState()
    {
        var fixture = CreateFixture("2.0.0", new byte[1024]);
        using var service = CreateService(new AsyncStubHandler(async (request, cancellationToken) =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path.EndsWith("/releases/latest", StringComparison.Ordinal))
            {
                return JsonResponse(fixture.Release);
            }
            if (path.EndsWith(".sha256", StringComparison.Ordinal))
            {
                return BytesResponse(fixture.ChecksumBytes);
            }

            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            throw new InvalidOperationException("Unreachable");
        }));
        await service.CheckAsync(true);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            service.DownloadAsync(cancellation.Token));

        Assert.Equal(UpdateState.Available, service.State);
        Assert.Null(service.ErrorMessage);
    }

    [Fact]
    public async Task InstallAsync_WhenPackageIsReady_WritesPlanAndStartsCopiedUpdater()
    {
        var fixture = CreateFixture("2.0.0", [1, 2, 3, 4]);
        ProcessStartInfo? capturedStartInfo = null;
        using var service = CreateService(request => RouteFixture(request, fixture),
            startProcess: startInfo => capturedStartInfo = startInfo);
        var updaterPath = Path.Combine(_root, "install", UpdateProtocol.UpdaterExecutableName);
        await File.WriteAllBytesAsync(updaterPath, [9, 8, 7]);
        await service.CheckAsync(true);
        await service.DownloadAsync();

        await service.InstallAsync();

        Assert.Equal(UpdateState.Installing, service.State);
        Assert.NotNull(capturedStartInfo);
        Assert.Equal("apply", capturedStartInfo.ArgumentList[0]);
        var planPath = capturedStartInfo.ArgumentList[2];
        var plan = JsonSerializer.Deserialize(await File.ReadAllBytesAsync(planPath),
            UpdateJsonContext.Default.UpdateApplyPlan);
        Assert.NotNull(plan);
        Assert.Equal("2.0.0", plan.TargetVersion);
        Assert.Equal(fixture.PackageBytes.Length, plan.PackageSize);
        Assert.True(File.Exists(capturedStartInfo.FileName));
    }

    private UpdateService CreateService(Func<HttpRequestMessage, HttpResponseMessage> response,
        string currentVersion = "1.0.0", Action<ProcessStartInfo>? startProcess = null)
    {
        return CreateService(new StubHandler(response), currentVersion, startProcess);
    }

    private UpdateService CreateService(HttpMessageHandler handler,
        string currentVersion = "1.0.0", Action<ProcessStartInfo>? startProcess = null)
    {
        var installRoot = Path.Combine(_root, "install");
        Directory.CreateDirectory(installRoot);
        var client = new HttpClient(handler)
        {
            Timeout = TimeSpan.FromSeconds(10)
        };
        return new UpdateService(new UpdateServiceOptions
        {
            GitHubOwner = Owner,
            GitHubRepository = Repository,
            GitHubApiBaseUrl = "https://api.github.com",
            InstallRoot = installRoot,
            CurrentVersion = currentVersion,
            LocalDataRoot = Path.Combine(_root, "local"),
            UpdaterPath = Path.Combine(installRoot, UpdateProtocol.UpdaterExecutableName)
        }, client, startProcess);
    }

    private static Fixture CreateFixture(string version, byte[]? packageBytes = null)
    {
        packageBytes ??= [1, 2, 3];
        var packageName = $"Prometheus-{version}-win-x64.zip";
        var hash = Convert.ToHexStringLower(SHA256.HashData(packageBytes));
        var checksumBytes = Encoding.ASCII.GetBytes($"{hash}  {packageName}");
        var tag = "v" + version;
        var baseUrl = $"https://github.com/{Owner}/{Repository}/releases/download/{tag}/";
        var release = new GitHubRelease
        {
            TagName = tag,
            Body = "Release notes",
            Assets =
            [
                new GitHubReleaseAsset
                {
                    Name = packageName,
                    Size = packageBytes.Length,
                    BrowserDownloadUrl = new Uri(baseUrl + packageName)
                },
                new GitHubReleaseAsset
                {
                    Name = packageName + ".sha256",
                    Size = checksumBytes.Length,
                    BrowserDownloadUrl = new Uri(baseUrl + packageName + ".sha256")
                }
            ]
        };
        return new Fixture(release, packageName, packageBytes, checksumBytes);
    }

    private static HttpResponseMessage RouteFixture(HttpRequestMessage request, Fixture fixture)
    {
        var path = request.RequestUri!.AbsolutePath;
        if (path.EndsWith("/releases/latest", StringComparison.Ordinal))
        {
            return JsonResponse(fixture.Release);
        }
        if (path.EndsWith(".sha256", StringComparison.Ordinal))
        {
            return BytesResponse(fixture.ChecksumBytes);
        }
        return BytesResponse(fixture.PackageBytes);
    }

    private static HttpResponseMessage JsonResponse(GitHubRelease release)
    {
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(JsonSerializer.Serialize(release,
                UpdateJsonContext.Default.GitHubRelease), Encoding.UTF8, "application/json")
        };
    }

    private static HttpResponseMessage BytesResponse(byte[] bytes,
        HttpStatusCode statusCode = HttpStatusCode.OK)
    {
        return new HttpResponseMessage(statusCode)
        {
            Content = new ByteArrayContent(bytes)
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

    private sealed class Fixture(GitHubRelease release, string packageName,
        byte[] packageBytes, byte[] checksumBytes)
    {
        public GitHubRelease Release { get; } = release;
        public string PackageName { get; } = packageName;
        public byte[] PackageBytes { get; } = packageBytes;
        public byte[] ChecksumBytes { get; set; } = checksumBytes;
    }
}

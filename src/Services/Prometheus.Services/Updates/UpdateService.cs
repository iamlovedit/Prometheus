#nullable enable

using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Prometheus.Services.Interfaces.Updates;
using Prometheus.Update;
using Serilog;

namespace Prometheus.Services.Updates;

public sealed class UpdateService : IUpdateService, IDisposable
{
    private readonly UpdateServiceOptions _options;
    private readonly HttpClient _httpClient;
    private readonly SemaphoreSlim _operationGate = new(1, 1);
    private readonly string _localDataRoot;
    private readonly Action<ProcessStartInfo> _startProcess;

    private GitHubRelease? _cachedRelease;
    private EntityTagHeaderValue? _releaseEtag;
    private GitHubReleaseSelection? _selection;
    private string? _packagePath;
    private string? _packageSha256;

    public UpdateService(UpdateServiceOptions options)
        : this(options, new HttpClient
        {
            Timeout = TimeSpan.FromMinutes(30)
        })
    {
    }

    internal UpdateService(UpdateServiceOptions options, HttpClient httpClient,
        Action<ProcessStartInfo>? startProcess = null)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("Prometheus-Updater/1.0");
        _localDataRoot = string.IsNullOrWhiteSpace(options.LocalDataRoot)
            ? UpdatePaths.GetLocalDataRoot()
            : Path.GetFullPath(options.LocalDataRoot);
        _startProcess = startProcess ?? (startInfo =>
        {
            _ = Process.Start(startInfo)
                ?? throw new InvalidOperationException("Unable to start the Prometheus updater.");
        });
        UpdateVersion.Parse(options.CurrentVersion);
    }

    public UpdateState State { get; private set; } = UpdateState.Idle;
    public double Progress { get; private set; }
    public string? ErrorMessage { get; private set; }
    public AvailableUpdate? AvailableUpdate { get; private set; }

    public event EventHandler<UpdateStateChangedEventArgs>? StateChanged;

    public async Task<AvailableUpdate?> CheckAsync(bool manual,
        CancellationToken cancellationToken = default)
    {
        if (!await _operationGate.WaitAsync(0, cancellationToken).ConfigureAwait(false))
        {
            return AvailableUpdate;
        }

        try
        {
            SetState(UpdateState.Checking, 0, null);
            if (string.IsNullOrWhiteSpace(_options.GitHubOwner)
                || string.IsNullOrWhiteSpace(_options.GitHubRepository))
            {
                if (manual)
                {
                    throw new InvalidOperationException(
                        "The GitHub update repository is not configured for this build.");
                }
                SetState(UpdateState.Idle, 0, null);
                return null;
            }

            var release = await RequestLatestReleaseAsync(cancellationToken)
                .ConfigureAwait(false);
            var selection = UpdateValidation.ValidateGitHubRelease(release,
                _options.GitHubOwner, _options.GitHubRepository);
            if (UpdateVersion.Parse(selection.Version)
                .CompareTo(UpdateVersion.Parse(_options.CurrentVersion)) <= 0)
            {
                _selection = null;
                _packagePath = null;
                _packageSha256 = null;
                AvailableUpdate = null;
                SetState(UpdateState.UpToDate, 0, null);
                return null;
            }

            _selection = selection;
            _packagePath = null;
            _packageSha256 = null;
            AvailableUpdate = new AvailableUpdate
            {
                Version = selection.Version,
                IsMandatory = false,
                ReleaseNotes = selection.Release.Body ?? string.Empty,
                DownloadSize = selection.Package.Size
            };
            SetState(UpdateState.Available, 0, null);
            return AvailableUpdate;
        }
        catch (OperationCanceledException)
        {
            SetState(AvailableUpdate is null ? UpdateState.Idle : UpdateState.Available,
                0, null);
            throw;
        }
        catch (Exception exception)
        {
            Log.Warning(exception, "Unable to check GitHub for Prometheus updates");
            if (manual)
            {
                SetState(UpdateState.Failed, 0, DescribeFailure(exception));
            }
            else
            {
                SetState(AvailableUpdate is null ? UpdateState.Idle : UpdateState.Available,
                    0, null);
            }
            return null;
        }
        finally
        {
            _operationGate.Release();
        }
    }

    public async Task DownloadAsync(CancellationToken cancellationToken = default)
    {
        await _operationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_selection is null || AvailableUpdate is null)
            {
                throw new InvalidOperationException("No update is available to download.");
            }

            SetState(UpdateState.Downloading, 0, null);
            var updateRoot = Path.Combine(_localDataRoot, "Updates", _selection.Version);
            Directory.CreateDirectory(updateRoot);
            var checksumContent = await DownloadChecksumAsync(_selection.Checksum,
                cancellationToken).ConfigureAwait(false);
            _packageSha256 = UpdateValidation.ParseSha256File(checksumContent,
                _selection.Package.Name);

            _packagePath = Path.Combine(updateRoot, _selection.Package.Name);
            var metadataPath = Path.Combine(updateRoot, "download.json");
            PreparePartialDownload(metadataPath, _packagePath, _selection);
            if (File.Exists(_packagePath))
            {
                try
                {
                    SetState(UpdateState.Verifying, 1, null);
                    await UpdateSecurity.VerifyFileAsync(_packagePath,
                        _selection.Package.Size, _packageSha256, cancellationToken)
                        .ConfigureAwait(false);
                    File.Delete(metadataPath);
                    SetState(UpdateState.ReadyToInstall, 1, null);
                    return;
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    Log.Warning(exception, "Discarding an invalid cached GitHub update package");
                    File.Delete(_packagePath);
                }
            }

            SetState(UpdateState.Downloading, 0, null);
            await DownloadPackageAsync(_selection.Package.BrowserDownloadUrl, _packagePath,
                _selection.Package.Size, cancellationToken).ConfigureAwait(false);
            SetState(UpdateState.Verifying, 1, null);
            try
            {
                await UpdateSecurity.VerifyFileAsync(_packagePath, _selection.Package.Size,
                    _packageSha256, cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                File.Delete(_packagePath);
                throw;
            }
            File.Delete(metadataPath);
            SetState(UpdateState.ReadyToInstall, 1, null);
        }
        catch (OperationCanceledException)
        {
            SetState(UpdateState.Available, 0, null);
            throw;
        }
        catch (Exception exception)
        {
            Log.Warning(exception, "Unable to download Prometheus update from GitHub");
            SetState(UpdateState.Failed, Progress, DescribeFailure(exception));
            throw;
        }
        finally
        {
            _operationGate.Release();
        }
    }

    public async Task InstallAsync(CancellationToken cancellationToken = default)
    {
        await _operationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (State != UpdateState.ReadyToInstall || _selection is null
                || _packagePath is null || _packageSha256 is null)
            {
                throw new InvalidOperationException("The update is not ready to install.");
            }

            cancellationToken.ThrowIfCancellationRequested();
            var updaterPath = string.IsNullOrWhiteSpace(_options.UpdaterPath)
                ? Path.Combine(_options.InstallRoot, UpdateProtocol.UpdaterExecutableName)
                : Path.GetFullPath(_options.UpdaterPath);
            if (!File.Exists(updaterPath))
            {
                throw new FileNotFoundException(
                    "Prometheus.Updater.exe was not found in the application directory.",
                    updaterPath);
            }

            var hostRoot = Path.Combine(_localDataRoot, "Updates", "host");
            Directory.CreateDirectory(hostRoot);
            CleanupStaleHosts(hostRoot);
            var hostPath = Path.Combine(hostRoot,
                $"Prometheus.Updater-{Guid.NewGuid():N}.exe");
            File.Copy(updaterPath, hostPath, true);
            var plan = new UpdateApplyPlan
            {
                InstallRoot = Path.GetFullPath(_options.InstallRoot),
                CurrentVersion = _options.CurrentVersion,
                TargetVersion = _selection.Version,
                ParentProcessId = Environment.ProcessId,
                HealthToken = Guid.NewGuid().ToString("D"),
                PackagePath = _packagePath,
                PackageSize = _selection.Package.Size,
                PackageSha256 = _packageSha256
            };
            UpdateValidation.ValidateApplyPlan(plan);
            var planPath = Path.Combine(hostRoot, $"apply-{Guid.NewGuid():N}.json");
            UpdatePaths.WriteJsonAtomic(planPath, plan, UpdateJsonContext.Default.UpdateApplyPlan);

            var startInfo = new ProcessStartInfo(hostPath)
            {
                UseShellExecute = false,
                WorkingDirectory = hostRoot
            };
            startInfo.ArgumentList.Add("apply");
            startInfo.ArgumentList.Add("--plan");
            startInfo.ArgumentList.Add(planPath);
            _startProcess(startInfo);
            SetState(UpdateState.Installing, 1, null);
        }
        catch (Exception exception)
        {
            Log.Warning(exception, "Unable to start Prometheus update installation");
            SetState(UpdateState.Failed, Progress, DescribeFailure(exception));
            throw;
        }
        finally
        {
            _operationGate.Release();
        }
    }

    public void Dispose()
    {
        _operationGate.Dispose();
        _httpClient.Dispose();
    }

    private async Task<GitHubRelease> RequestLatestReleaseAsync(
        CancellationToken cancellationToken)
    {
        var baseUri = new Uri(_options.GitHubApiBaseUrl.TrimEnd('/') + "/",
            UriKind.Absolute);
        if (!string.Equals(baseUri.Scheme, Uri.UriSchemeHttps,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("The GitHub API URL must use HTTPS.");
        }

        var requestUri = new Uri(baseUri,
            $"repos/{Uri.EscapeDataString(_options.GitHubOwner)}/"
            + $"{Uri.EscapeDataString(_options.GitHubRepository)}/releases/latest");
        using var request = new HttpRequestMessage(HttpMethod.Get, requestUri);
        request.Headers.Accept.ParseAdd(UpdateProtocol.GitHubApiAccept);
        request.Headers.TryAddWithoutValidation("X-GitHub-Api-Version",
            UpdateProtocol.GitHubApiVersion);
        if (_releaseEtag is not null)
        {
            request.Headers.IfNoneMatch.Add(_releaseEtag);
        }

        using var response = await _httpClient.SendAsync(request,
            HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.NotModified)
        {
            return _cachedRelease
                ?? throw new InvalidDataException(
                    "GitHub returned 304 without a cached Release response.");
        }
        if (response.StatusCode is HttpStatusCode.Forbidden
            or HttpStatusCode.TooManyRequests)
        {
            throw new HttpRequestException(
                "GitHub API rate limit was reached. Please try again later.", null,
                response.StatusCode);
        }
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken)
            .ConfigureAwait(false);
        var release = await JsonSerializer.DeserializeAsync(stream,
            UpdateJsonContext.Default.GitHubRelease, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidDataException("GitHub returned an empty Release response.");
        _cachedRelease = release;
        _releaseEtag = response.Headers.ETag;
        return release;
    }

    private async Task<string> DownloadChecksumAsync(GitHubReleaseAsset asset,
        CancellationToken cancellationToken)
    {
        using var response = await _httpClient.GetAsync(asset.BrowserDownloadUrl,
            HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength is long contentLength
            && contentLength != asset.Size)
        {
            throw new InvalidDataException("The GitHub checksum asset size does not match.");
        }
        var bytes = new byte[checked((int)asset.Size)];
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken)
            .ConfigureAwait(false);
        var total = 0;
        while (total < bytes.Length)
        {
            var read = await stream.ReadAsync(bytes.AsMemory(total), cancellationToken)
                .ConfigureAwait(false);
            if (read == 0)
            {
                throw new EndOfStreamException(
                    "The GitHub checksum download ended before the advertised asset size.");
            }
            total += read;
        }
        var extra = new byte[1];
        if (await stream.ReadAsync(extra, cancellationToken).ConfigureAwait(false) != 0)
        {
            throw new InvalidDataException("The GitHub checksum asset size does not match.");
        }
        return new UTF8Encoding(false, true).GetString(bytes);
    }

    private static void CleanupStaleHosts(string hostRoot)
    {
        var staleBefore = DateTime.UtcNow - TimeSpan.FromHours(1);
        foreach (var pattern in new[] { "Prometheus.Updater-*.exe", "apply-*.json" })
        {
            foreach (var path in Directory.EnumerateFiles(hostRoot, pattern,
                         SearchOption.TopDirectoryOnly))
            {
                if (File.GetLastWriteTimeUtc(path) >= staleBefore)
                {
                    continue;
                }
                try
                {
                    File.Delete(path);
                }
                catch (IOException)
                {
                    // A previous updater may still be finishing; it will be retried next time.
                }
                catch (UnauthorizedAccessException)
                {
                    // A locked or protected stale host must not block the current update.
                }
            }
        }
    }

    private static void PreparePartialDownload(string metadataPath, string packagePath,
        GitHubReleaseSelection selection)
    {
        var partialPath = packagePath + ".part";
        var matches = false;
        if (File.Exists(metadataPath))
        {
            try
            {
                var metadata = JsonSerializer.Deserialize(File.ReadAllBytes(metadataPath),
                    UpdateJsonContext.Default.UpdateDownloadMetadata);
                matches = metadata is not null
                    && metadata.SchemaVersion == UpdateProtocol.SchemaVersion
                    && string.Equals(metadata.Version, selection.Version,
                        StringComparison.Ordinal)
                    && string.Equals(metadata.AssetName, selection.Package.Name,
                        StringComparison.Ordinal)
                    && metadata.AssetSize == selection.Package.Size;
            }
            catch (JsonException)
            {
                matches = false;
            }
        }

        if (!matches)
        {
            File.Delete(partialPath);
        }
        UpdatePaths.WriteJsonAtomic(metadataPath, new UpdateDownloadMetadata
        {
            Version = selection.Version,
            AssetName = selection.Package.Name,
            AssetSize = selection.Package.Size
        }, UpdateJsonContext.Default.UpdateDownloadMetadata);
    }

    private async Task DownloadPackageAsync(Uri url, string destination, long expectedSize,
        CancellationToken cancellationToken)
    {
        var partialPath = destination + ".part";
        var existingLength = File.Exists(partialPath) ? new FileInfo(partialPath).Length : 0;
        if (existingLength > expectedSize)
        {
            File.Delete(partialPath);
            existingLength = 0;
        }
        else if (existingLength == expectedSize)
        {
            File.Move(partialPath, destination, true);
            return;
        }

        for (var attempt = 0; attempt < 2; attempt++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            if (existingLength > 0)
            {
                request.Headers.Range = new RangeHeaderValue(existingLength, null);
            }
            using var response = await _httpClient.SendAsync(request,
                HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false);
            if (existingLength > 0
                && response.StatusCode == HttpStatusCode.RequestedRangeNotSatisfiable)
            {
                File.Delete(partialPath);
                existingLength = 0;
                continue;
            }
            response.EnsureSuccessStatusCode();

            var append = existingLength > 0
                && response.StatusCode == HttpStatusCode.PartialContent;
            if (append)
            {
                var range = response.Content.Headers.ContentRange;
                if (range?.From != existingLength
                    || range.To is not long rangeEnd || rangeEnd >= expectedSize
                    || range.Length is long rangeLength && rangeLength != expectedSize)
                {
                    throw new InvalidDataException(
                        "GitHub returned an invalid update byte range.");
                }
            }
            else
            {
                existingLength = 0;
            }

            var total = existingLength;
            await using (var target = new FileStream(partialPath,
                             append ? FileMode.Append : FileMode.Create, FileAccess.Write,
                             FileShare.None, 128 * 1024,
                             FileOptions.Asynchronous | FileOptions.SequentialScan))
            await using (var source = await response.Content.ReadAsStreamAsync(cancellationToken)
                             .ConfigureAwait(false))
            {
                var buffer = new byte[128 * 1024];
                int read;
                while ((read = await source.ReadAsync(buffer, cancellationToken)
                           .ConfigureAwait(false)) > 0)
                {
                    await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken)
                        .ConfigureAwait(false);
                    total += read;
                    if (total > expectedSize)
                    {
                        throw new InvalidDataException(
                            "GitHub returned more data than the Release Asset size.");
                    }
                    SetState(UpdateState.Downloading,
                        Math.Clamp((double)total / expectedSize, 0, 1), null);
                }
                await target.FlushAsync(cancellationToken).ConfigureAwait(false);
            }

            if (total != expectedSize)
            {
                throw new EndOfStreamException(
                    $"The update download ended at {total} of {expectedSize} bytes.");
            }
            File.Move(partialPath, destination, true);
            return;
        }

        throw new InvalidOperationException("Unable to resume the GitHub update download.");
    }

    private static string DescribeFailure(Exception exception)
    {
        var isChinese = string.Equals(CultureInfo.CurrentUICulture.TwoLetterISOLanguageName,
            "zh", StringComparison.OrdinalIgnoreCase);
        return exception switch
        {
            HttpRequestException { StatusCode: HttpStatusCode.Forbidden
                or HttpStatusCode.TooManyRequests } =>
                isChinese ? "GitHub 请求已达到频率限制，请稍后重试。"
                    : "The GitHub API rate limit was reached. Please try again later.",
            HttpRequestException => isChinese
                ? "无法连接 GitHub，请检查网络连接后重试。"
                : "Unable to connect to GitHub. Check the network connection and try again.",
            JsonException or InvalidDataException =>
                isChinese ? "GitHub Release 数据或更新文件无效，无法继续更新。"
                    : "The GitHub Release data or update file is invalid.",
            FileNotFoundException => isChinese
                ? "未找到更新程序，请重新安装最新版本后再试。"
                : "The updater was not found. Reinstall the latest version and try again.",
            _ => exception.Message
        };
    }

    private void SetState(UpdateState state, double progress, string? error)
    {
        State = state;
        Progress = progress;
        ErrorMessage = error;
        StateChanged?.Invoke(this, new UpdateStateChangedEventArgs(state, progress, error));
    }
}

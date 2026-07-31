#nullable enable

using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
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
    private readonly string _installationId;
    private readonly string _localDataRoot;
    private readonly Func<SignedEnvelope, ReleaseDescriptor> _verifyRelease;
    private readonly Func<SignedEnvelope, ReleaseManifest> _verifyManifest;

    private UpdateApiResponse? _apiResponse;
    private ReleaseDescriptor? _descriptor;
    private string? _packagePath;
    private string? _manifestPath;
    private string? _bootstrapperPath;
    private string? _selectedArtifactId;
    private UpdatePackageKind _packageKind;

    public UpdateService(UpdateServiceOptions options)
        : this(options, new HttpClient
        {
            Timeout = TimeSpan.FromMinutes(30)
        })
    {
    }

    internal UpdateService(UpdateServiceOptions options, HttpClient httpClient,
        Func<SignedEnvelope, ReleaseDescriptor>? verifyRelease = null,
        Func<SignedEnvelope, ReleaseManifest>? verifyManifest = null)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("Prometheus-Updater/1.0");
        _localDataRoot = string.IsNullOrWhiteSpace(options.LocalDataRoot)
            ? UpdatePaths.GetLocalDataRoot()
            : Path.GetFullPath(options.LocalDataRoot);
        _verifyRelease = verifyRelease ?? (envelope => UpdateSecurity.VerifyAndDeserialize(
            envelope, UpdateJsonContext.Default.ReleaseDescriptor));
        _verifyManifest = verifyManifest ?? (envelope => UpdateSecurity.VerifyAndDeserialize(
            envelope, UpdateJsonContext.Default.ReleaseManifest));
        UpdateVersion.Parse(options.CurrentVersion);
        UpdateVersion.Parse(options.BootstrapperVersion);
        _installationId = LoadOrCreateInstallationId();
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
            if (string.IsNullOrWhiteSpace(_options.ApiBaseUrl))
            {
                if (manual)
                {
                    throw new InvalidOperationException(
                        "The update API is not configured for this build.");
                }
                SetState(UpdateState.Idle, 0, null);
                return null;
            }

            _apiResponse = await RequestUpdateAsync(cancellationToken).ConfigureAwait(false);
            if (_apiResponse is null)
            {
                AvailableUpdate = null;
                SetState(UpdateState.UpToDate, 0, null);
                return null;
            }

            _descriptor = _verifyRelease(_apiResponse.Release);
            ValidateDescriptor(_descriptor);
            if (UpdateVersion.Parse(_descriptor.MinimumBootstrapperVersion)
                .CompareTo(UpdateVersion.Parse(_options.BootstrapperVersion)) > 0)
            {
                throw new InvalidOperationException(
                    "This update requires a newer Prometheus bootstrapper. Download the latest portable package.");
            }

            var selected = ResolveSelectedArtifact(_descriptor, _apiResponse.SelectedArtifactId,
                _options.CurrentVersion);
            var mandatory = UpdateVersion.Parse(_options.CurrentVersion)
                .CompareTo(UpdateVersion.Parse(_descriptor.MinimumSupportedVersion)) < 0;
            AvailableUpdate = new AvailableUpdate
            {
                Descriptor = _descriptor,
                ApiResponse = _apiResponse,
                IsMandatory = mandatory,
                ReleaseNotes = ResolveReleaseNotes(_descriptor),
                DownloadSize = selected.Size
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
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            Log.Warning(exception, "Unable to check for Prometheus updates");
            if (manual)
            {
                SetState(UpdateState.Failed, 0, exception.Message);
            }
            else
            {
                SetState(UpdateState.Idle, 0, null);
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
            if (_descriptor is null || _apiResponse is null || AvailableUpdate is null)
            {
                throw new InvalidOperationException("No update is available to download.");
            }

            SetState(UpdateState.Downloading, 0, null);
            var updateRoot = Path.Combine(_localDataRoot, "Updates",
                _descriptor.Version);
            Directory.CreateDirectory(updateRoot);

            _manifestPath = Path.Combine(updateRoot, "manifest.json");
            await DownloadFileWithRefreshAsync(DownloadObject.Manifest, _manifestPath,
                _descriptor.TargetManifest, cancellationToken).ConfigureAwait(false);
            var manifestEnvelope = JsonSerializer.Deserialize(
                await File.ReadAllBytesAsync(_manifestPath, cancellationToken).ConfigureAwait(false),
                UpdateJsonContext.Default.SignedEnvelope)
                ?? throw new InvalidDataException("The downloaded manifest is empty.");
            var targetManifest = _verifyManifest(manifestEnvelope);
            UpdateValidation.ValidateManifest(targetManifest, _descriptor.Version);

            var selectedArtifact = ResolveSelectedArtifact(_descriptor,
                _apiResponse.SelectedArtifactId, _options.CurrentVersion);
            _selectedArtifactId = selectedArtifact.Id;
            _packageKind = selectedArtifact.Id.StartsWith("delta:",
                StringComparison.OrdinalIgnoreCase)
                ? UpdatePackageKind.Delta
                : UpdatePackageKind.Full;
            var packageUrlKind = DownloadObject.SelectedPackage;

            if (_packageKind == UpdatePackageKind.Delta
                && !await IsInstalledBaseValidAsync(cancellationToken).ConfigureAwait(false))
            {
                selectedArtifact = _descriptor.FullPackage;
                _selectedArtifactId = selectedArtifact.Id;
                _packageKind = UpdatePackageKind.Full;
                packageUrlKind = DownloadObject.FullPackage;
            }

            _packagePath = Path.Combine(updateRoot,
                _packageKind == UpdatePackageKind.Delta ? "delta.zip" : "full.zip");
            await DownloadFileWithRefreshAsync(packageUrlKind, _packagePath, selectedArtifact,
                cancellationToken, reportProgress: true).ConfigureAwait(false);

            _bootstrapperPath = null;
            if (_descriptor.Bootstrapper is not null)
            {
                if (_apiResponse.BootstrapperUrl is null)
                {
                    throw new InvalidDataException(
                        "The update API did not provide the required bootstrapper download URL.");
                }
                _bootstrapperPath = Path.Combine(updateRoot, "Prometheus.exe");
                await DownloadFileWithRefreshAsync(DownloadObject.Bootstrapper,
                    _bootstrapperPath, _descriptor.Bootstrapper, cancellationToken)
                    .ConfigureAwait(false);
            }

            SetState(UpdateState.ReadyToInstall, 1, null);
        }
        catch (OperationCanceledException)
        {
            SetState(UpdateState.Available, 0, null);
            throw;
        }
        catch (Exception exception)
        {
            Log.Warning(exception, "Unable to download Prometheus update");
            SetState(UpdateState.Failed, Progress, exception.Message);
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
            if (State != UpdateState.ReadyToInstall || _descriptor is null
                || _apiResponse is null || _packagePath is null || _manifestPath is null
                || _selectedArtifactId is null)
            {
                throw new InvalidOperationException("The update is not ready to install.");
            }

            var rootBootstrapper = Path.Combine(_options.InstallRoot,
                UpdateProtocol.BootstrapperExecutableName);
            if (!File.Exists(rootBootstrapper))
            {
                throw new FileNotFoundException(
                    "The Prometheus bootstrapper was not found. Install the portable bootstrap package first.",
                    rootBootstrapper);
            }

            var updatesRoot = Path.Combine(_localDataRoot, "Updates");
            Directory.CreateDirectory(updatesRoot);
            var hostPath = Path.Combine(updatesRoot,
                $"Prometheus.UpdateHost-{Guid.NewGuid():N}.exe");
            File.Copy(rootBootstrapper, hostPath, true);
            var plan = new UpdateApplyPlan
            {
                InstallRoot = _options.InstallRoot,
                CurrentVersion = _options.CurrentVersion,
                TargetVersion = _descriptor.Version,
                ParentProcessId = Environment.ProcessId,
                HealthToken = Guid.NewGuid().ToString("D"),
                PackageKind = _packageKind,
                SelectedArtifactId = _selectedArtifactId,
                PackagePath = _packagePath,
                TargetManifestPath = _manifestPath,
                BootstrapperPath = _bootstrapperPath,
                Release = _apiResponse.Release
            };
            var planPath = Path.Combine(updatesRoot, $"apply-{Guid.NewGuid():N}.json");
            UpdatePaths.WriteJsonAtomic(planPath, plan, UpdateJsonContext.Default.UpdateApplyPlan);

            var startInfo = new ProcessStartInfo(hostPath)
            {
                UseShellExecute = false,
                WorkingDirectory = updatesRoot
            };
            startInfo.ArgumentList.Add("apply");
            startInfo.ArgumentList.Add("--plan");
            startInfo.ArgumentList.Add(planPath);
            _ = Process.Start(startInfo)
                ?? throw new InvalidOperationException("Unable to start the Prometheus updater.");
            SetState(UpdateState.Installing, 1, null);
        }
        catch (Exception exception)
        {
            Log.Warning(exception, "Unable to start Prometheus update installation");
            SetState(UpdateState.Failed, Progress, exception.Message);
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

    private async Task<UpdateApiResponse?> RequestUpdateAsync(
        CancellationToken cancellationToken)
    {
        var baseUri = new Uri(_options.ApiBaseUrl.TrimEnd('/') + "/", UriKind.Absolute);
        var requestUri = new Uri(baseUri,
            "api/v1/updates/windows"
            + $"?currentVersion={Uri.EscapeDataString(_options.CurrentVersion)}"
            + $"&channel={UpdateProtocol.StableChannel}"
            + $"&rid={UpdateProtocol.WindowsX64Rid}"
            + $"&installationId={Uri.EscapeDataString(_installationId)}");
        using var response = await _httpClient.GetAsync(requestUri,
            HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.NoContent)
        {
            return null;
        }
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken)
            .ConfigureAwait(false);
        return await JsonSerializer.DeserializeAsync(stream,
            UpdateJsonContext.Default.UpdateApiResponse, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidDataException("The update API returned an empty response.");
    }

    private async Task DownloadFileWithRefreshAsync(DownloadObject downloadObject,
        string destination, UpdateArtifact artifact, CancellationToken cancellationToken,
        bool reportProgress = false)
    {
        if (File.Exists(destination))
        {
            try
            {
                await UpdateSecurity.VerifyFileAsync(destination, artifact.Size, artifact.Sha256,
                    cancellationToken).ConfigureAwait(false);
                return;
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                Log.Warning(exception, "Discarding an invalid cached update artifact");
                File.Delete(destination);
            }
        }

        for (var attempt = 0; attempt < 2; attempt++)
        {
            var url = ResolveUrl(downloadObject);
            try
            {
                await DownloadFileAsync(url, destination, artifact.Size, cancellationToken,
                    reportProgress).ConfigureAwait(false);
                await UpdateSecurity.VerifyFileAsync(destination, artifact.Size, artifact.Sha256,
                    cancellationToken).ConfigureAwait(false);
                return;
            }
            catch (ExpiredDownloadException) when (attempt == 0)
            {
                var previousResponse = _apiResponse
                    ?? throw new InvalidOperationException(
                        "No update API response is available.");
                var refreshed = await RequestUpdateAsync(cancellationToken).ConfigureAwait(false)
                    ?? throw new InvalidOperationException("The update is no longer available.");
                var refreshedDescriptor = _verifyRelease(refreshed.Release);
                ValidateDescriptor(refreshedDescriptor);
                var refreshedArtifact = ResolveArtifactForDownload(downloadObject,
                    refreshedDescriptor, refreshed.SelectedArtifactId);
                if (_descriptor is null
                    || !string.Equals(refreshed.Release.Payload,
                        previousResponse.Release.Payload, StringComparison.Ordinal)
                    || !string.Equals(refreshed.SelectedArtifactId,
                        previousResponse.SelectedArtifactId, StringComparison.Ordinal)
                    || !UpdateValidation.ArtifactsMatch(artifact, refreshedArtifact))
                {
                    throw new InvalidDataException(
                        "The update object changed while refreshing its download URL.");
                }
                _apiResponse = refreshed;
            }
        }

        throw new InvalidOperationException("Unable to download the update file.");
    }

    private async Task DownloadFileAsync(Uri url, string destination, long expectedSize,
        CancellationToken cancellationToken, bool reportProgress)
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

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        if (existingLength > 0)
        {
            request.Headers.Range = new RangeHeaderValue(existingLength, null);
        }
        using var response = await _httpClient.SendAsync(request,
            HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.Forbidden)
        {
            throw new ExpiredDownloadException();
        }
        response.EnsureSuccessStatusCode();

        var append = existingLength > 0 && response.StatusCode == HttpStatusCode.PartialContent;
        if (append && (response.Content.Headers.ContentRange?.From != existingLength
                       || response.Content.Headers.ContentRange.To is long rangeEnd
                       && rangeEnd >= expectedSize))
        {
            throw new InvalidDataException("The update server returned an invalid byte range.");
        }
        if (!append)
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
                        "The update server returned more data than the signed artifact size.");
                }
                if (reportProgress && expectedSize > 0)
                {
                    SetState(UpdateState.Downloading,
                        Math.Clamp((double)total / expectedSize, 0, 1), null);
                }
            }

            await target.FlushAsync(cancellationToken).ConfigureAwait(false);
        }
        if (total != expectedSize)
        {
            throw new EndOfStreamException(
                "The update download ended before the signed artifact size was reached.");
        }
        File.Move(partialPath, destination, true);
    }

    private async Task<bool> IsInstalledBaseValidAsync(CancellationToken cancellationToken)
    {
        try
        {
            var versionRoot = Path.Combine(_options.InstallRoot, "versions",
                _options.CurrentVersion);
            var manifestPath = Path.Combine(versionRoot,
                UpdateProtocol.InstalledManifestFileName);
            var envelope = JsonSerializer.Deserialize(await File.ReadAllBytesAsync(manifestPath,
                    cancellationToken).ConfigureAwait(false),
                UpdateJsonContext.Default.SignedEnvelope)
                ?? throw new InvalidDataException("The installed manifest is empty.");
            var manifest = _verifyManifest(envelope);
            UpdateValidation.ValidateManifest(manifest, _options.CurrentVersion);

            foreach (var file in manifest.Files)
            {
                var path = UpdatePaths.ResolveUnderRoot(versionRoot, file.Path);
                await UpdateSecurity.VerifyFileAsync(path, file.Size, file.Sha256,
                    cancellationToken).ConfigureAwait(false);
            }
            return true;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            Log.Warning(exception,
                "Installed version failed update-manifest verification; using full package");
            return false;
        }
    }

    private Uri ResolveUrl(DownloadObject value)
    {
        var response = _apiResponse
            ?? throw new InvalidOperationException("No update API response is available.");
        return value switch
        {
            DownloadObject.Manifest => response.ManifestUrl,
            DownloadObject.SelectedPackage => response.PackageUrl,
            DownloadObject.FullPackage => response.FullPackageUrl,
            DownloadObject.Bootstrapper => response.BootstrapperUrl
                ?? throw new InvalidOperationException("No bootstrapper URL was supplied."),
            _ => throw new ArgumentOutOfRangeException(nameof(value))
        };
    }

    private void ValidateDescriptor(ReleaseDescriptor descriptor)
    {
        UpdateValidation.ValidateReleaseDescriptor(descriptor, _options.CurrentVersion);
    }

    private static UpdateArtifact ResolveSelectedArtifact(ReleaseDescriptor descriptor, string id,
        string currentVersion)
    {
        if (string.Equals(id, descriptor.FullPackage.Id, StringComparison.Ordinal))
        {
            return descriptor.FullPackage;
        }
        var delta = descriptor.Deltas.FirstOrDefault(value =>
            string.Equals(value.Id, id, StringComparison.Ordinal));
        if (delta is null)
        {
            throw new InvalidDataException("The API selected an unauthorized update package.");
        }
        if (!string.Equals(delta.BaseVersion, currentVersion, StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                "The API selected a delta for a different installed version.");
        }
        return new UpdateArtifact
        {
            Id = delta.Id,
            ObjectKey = delta.ObjectKey,
            Size = delta.Size,
            Sha256 = delta.Sha256
        };
    }

    private UpdateArtifact ResolveArtifactForDownload(DownloadObject downloadObject,
        ReleaseDescriptor descriptor, string selectedArtifactId)
    {
        return downloadObject switch
        {
            DownloadObject.Manifest => descriptor.TargetManifest,
            DownloadObject.SelectedPackage => ResolveSelectedArtifact(descriptor,
                selectedArtifactId, _options.CurrentVersion),
            DownloadObject.FullPackage => descriptor.FullPackage,
            DownloadObject.Bootstrapper => descriptor.Bootstrapper
                ?? throw new InvalidDataException(
                    "The refreshed release does not contain a bootstrapper artifact."),
            _ => throw new ArgumentOutOfRangeException(nameof(downloadObject))
        };
    }

    private static string ResolveReleaseNotes(ReleaseDescriptor descriptor)
    {
        var culture = CultureInfo.CurrentUICulture.Name;
        if (descriptor.ReleaseNotes.TryGetValue(culture, out var value))
        {
            return value;
        }
        if (descriptor.ReleaseNotes.TryGetValue("en-US", out value))
        {
            return value;
        }
        return descriptor.ReleaseNotes.Values.FirstOrDefault() ?? string.Empty;
    }

    private string LoadOrCreateInstallationId()
    {
        var path = Path.Combine(_localDataRoot, "installation-id");
        try
        {
            if (File.Exists(path))
            {
                var value = File.ReadAllText(path).Trim();
                if (Guid.TryParse(value, out var existing))
                {
                    return existing.ToString("D");
                }
            }

            var id = Guid.NewGuid().ToString("D");
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path + ".tmp", id);
            File.Move(path + ".tmp", path, true);
            return id;
        }
        catch (Exception exception)
        {
            Log.Warning(exception, "Unable to persist Prometheus update installation ID");
            return Guid.NewGuid().ToString("D");
        }
    }

    private void SetState(UpdateState state, double progress, string? error)
    {
        State = state;
        Progress = progress;
        ErrorMessage = error;
        StateChanged?.Invoke(this, new UpdateStateChangedEventArgs(state, progress, error));
    }

    private enum DownloadObject
    {
        Manifest,
        SelectedPackage,
        FullPackage,
        Bootstrapper
    }

    private sealed class ExpiredDownloadException : Exception
    {
    }
}

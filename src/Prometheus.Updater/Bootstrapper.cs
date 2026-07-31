using System.Diagnostics;
using System.Text.Json;
using Prometheus.Update;

namespace Prometheus.Updater;

internal static class Bootstrapper
{
    private static readonly TimeSpan HealthTimeout = TimeSpan.FromSeconds(60);

    public static int LaunchCurrent()
    {
        var installRoot = Path.GetFullPath(AppContext.BaseDirectory);
        var state = BootstrapperStateStore.Load(installRoot);
        RecoverPendingState(installRoot, state);
        StartDesktop(installRoot, state.CurrentVersion, null);
        return 0;
    }

    public static async Task<int> ApplyAsync(string planPath)
    {
        return await ApplyAsync(planPath, null).ConfigureAwait(false);
    }

    internal static async Task<int> ApplyAsync(string planPath, BootstrapperTestHooks? hooks)
    {
        var plan = JsonSerializer.Deserialize(await File.ReadAllBytesAsync(planPath),
            UpdateJsonContext.Default.UpdateApplyPlan)
            ?? throw new InvalidDataException("The update plan is empty.");
        ValidatePlan(plan);
        await WaitForParentExitAsync(plan.ParentProcessId).ConfigureAwait(false);

        var descriptor = hooks?.VerifyRelease is null
            ? UpdateSecurity.VerifyAndDeserialize(plan.Release,
                UpdateJsonContext.Default.ReleaseDescriptor)
            : hooks.VerifyRelease(plan.Release);
        ValidateDescriptor(plan, descriptor);
        await UpdateInstaller.VerifyArtifactAsync(plan.TargetManifestPath,
            descriptor.TargetManifest).ConfigureAwait(false);
        var manifestEnvelope = JsonSerializer.Deserialize(
            await File.ReadAllBytesAsync(plan.TargetManifestPath).ConfigureAwait(false),
            UpdateJsonContext.Default.SignedEnvelope)
            ?? throw new InvalidDataException("The target manifest envelope is empty.");
        var targetManifest = hooks?.VerifyManifest is null
            ? UpdateSecurity.VerifyAndDeserialize(manifestEnvelope,
                UpdateJsonContext.Default.ReleaseManifest)
            : hooks.VerifyManifest(manifestEnvelope);
        UpdateInstaller.ValidateTargetManifest(plan, targetManifest);

        var packageArtifact = UpdateInstaller.ResolveSelectedArtifact(descriptor,
            plan.SelectedArtifactId);
        ValidateSelectedPackage(plan, descriptor, packageArtifact);
        await UpdateInstaller.VerifyArtifactAsync(plan.PackagePath, packageArtifact)
            .ConfigureAwait(false);

        var installRoot = Path.GetFullPath(plan.InstallRoot);
        var state = BootstrapperStateStore.Load(installRoot);
        if (!string.Equals(state.CurrentVersion, plan.CurrentVersion, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "The installed version changed while the update was downloading.");
        }
        if (UpdateVersion.Parse(state.BootstrapperVersion).CompareTo(
                UpdateVersion.Parse(descriptor.MinimumBootstrapperVersion)) < 0)
        {
            throw new InvalidOperationException(
                "This update requires a newer bootstrapper and must be installed from a portable package.");
        }

        var targetRoot = await UpdateInstaller.BuildTargetVersionAsync(plan, targetManifest)
            .ConfigureAwait(false);
        var installedBootstrapperVersion = await ReplaceBootstrapperIfNeededAsync(plan,
            descriptor, installRoot, state.BootstrapperVersion).ConfigureAwait(false);

        Process? process = null;
        var switched = false;
        try
        {
            BootstrapperStateStore.Save(installRoot, new BootstrapperState
            {
                CurrentVersion = plan.TargetVersion,
                RollbackVersion = plan.CurrentVersion,
                PendingHealthToken = plan.HealthToken,
                BootstrapperVersion = installedBootstrapperVersion
            });
            switched = true;

            var markerPath = UpdatePaths.GetHealthMarkerPath(plan.HealthToken);
            Directory.CreateDirectory(Path.GetDirectoryName(markerPath)!);
            File.Delete(markerPath);
            process = StartDesktop(installRoot, plan.TargetVersion, plan.HealthToken, hooks);
            var isHealthy = hooks?.WaitForHealth is null
                ? await WaitForHealthAsync(process, markerPath).ConfigureAwait(false)
                : await hooks.WaitForHealth(process, markerPath).ConfigureAwait(false);
            if (!isHealthy)
            {
                throw new InvalidOperationException(
                    "The updated desktop application failed its health check.");
            }

            BootstrapperStateStore.Save(installRoot, new BootstrapperState
            {
                CurrentVersion = plan.TargetVersion,
                RollbackVersion = plan.CurrentVersion,
                BootstrapperVersion = installedBootstrapperVersion
            });
            TryDelete(markerPath);
            UpdateInstaller.CleanupOldVersions(Path.GetDirectoryName(targetRoot)!,
                plan.TargetVersion, plan.CurrentVersion);
            BootstrapperLog.Write(
                $"Updated Prometheus from {plan.CurrentVersion} to {plan.TargetVersion}.");
            return 0;
        }
        catch (Exception exception) when (switched)
        {
            if (process is not null)
            {
                TryTerminate(process);
            }
            BootstrapperStateStore.Save(installRoot, new BootstrapperState
            {
                CurrentVersion = plan.CurrentVersion,
                RollbackVersion = plan.TargetVersion,
                BootstrapperVersion = installedBootstrapperVersion
            });
            using var rollbackProcess = StartDesktop(installRoot, plan.CurrentVersion, null,
                hooks);
            BootstrapperLog.Write(
                $"Update to {plan.TargetVersion} was rolled back: {exception}");
            return 2;
        }
        finally
        {
            process?.Dispose();
        }
    }

    private static void RecoverPendingState(string installRoot, BootstrapperState state)
    {
        if (string.IsNullOrWhiteSpace(state.PendingHealthToken))
        {
            return;
        }

        var markerPath = UpdatePaths.GetHealthMarkerPath(state.PendingHealthToken);
        if (File.Exists(markerPath))
        {
            state.PendingHealthToken = null;
            BootstrapperStateStore.Save(installRoot, state);
            TryDelete(markerPath);
            return;
        }

        if (!string.IsNullOrWhiteSpace(state.RollbackVersion))
        {
            (state.CurrentVersion, state.RollbackVersion) =
                (state.RollbackVersion, state.CurrentVersion);
            state.PendingHealthToken = null;
            BootstrapperStateStore.Save(installRoot, state);
            BootstrapperLog.Write(
                "Recovered an interrupted update by restoring the rollback version.");
        }
    }

    private static async Task<string> ReplaceBootstrapperIfNeededAsync(UpdateApplyPlan plan,
        ReleaseDescriptor descriptor, string installRoot, string currentBootstrapperVersion)
    {
        if (descriptor.Bootstrapper is null)
        {
            if (!string.IsNullOrWhiteSpace(plan.BootstrapperPath))
            {
                throw new InvalidDataException(
                    "The update plan contains an unauthorized bootstrapper artifact.");
            }
            return currentBootstrapperVersion;
        }
        if (string.IsNullOrWhiteSpace(plan.BootstrapperPath))
        {
            throw new InvalidDataException(
                "The update plan is missing the required bootstrapper artifact.");
        }

        await UpdateInstaller.VerifyArtifactAsync(plan.BootstrapperPath,
            descriptor.Bootstrapper).ConfigureAwait(false);
        var destination = Path.Combine(installRoot, UpdateProtocol.BootstrapperExecutableName);
        var temporaryPath = destination + ".new";
        File.Copy(plan.BootstrapperPath, temporaryPath, true);
        File.Move(temporaryPath, destination, true);
        return descriptor.BootstrapperVersion;
    }

    private static Process StartDesktop(string installRoot, string version, string? healthToken)
    {
        return StartDesktop(installRoot, version, healthToken, null);
    }

    private static Process StartDesktop(string installRoot, string version, string? healthToken,
        BootstrapperTestHooks? hooks)
    {
        if (hooks?.StartDesktop is not null)
        {
            return hooks.StartDesktop(installRoot, version, healthToken);
        }

        UpdateVersion.Parse(version);
        var versionRoot = Path.Combine(installRoot, "versions", version);
        var executable = Path.Combine(versionRoot, UpdateProtocol.DesktopExecutableName);
        if (!File.Exists(executable))
        {
            throw new FileNotFoundException(
                "The Prometheus desktop executable was not found.", executable);
        }

        var startInfo = new ProcessStartInfo(executable)
        {
            WorkingDirectory = versionRoot,
            UseShellExecute = false
        };
        if (!string.IsNullOrWhiteSpace(healthToken))
        {
            startInfo.ArgumentList.Add("--health-token");
            startInfo.ArgumentList.Add(healthToken);
        }

        return Process.Start(startInfo)
            ?? throw new InvalidOperationException("Unable to start Prometheus.Desktop.exe.");
    }

    private static async Task<bool> WaitForHealthAsync(Process process, string markerPath)
    {
        var deadline = DateTimeOffset.UtcNow + HealthTimeout;
        while (DateTimeOffset.UtcNow < deadline)
        {
            if (File.Exists(markerPath))
            {
                return true;
            }
            if (process.HasExited)
            {
                return false;
            }
            await Task.Delay(250).ConfigureAwait(false);
        }
        return false;
    }

    private static async Task WaitForParentExitAsync(int processId)
    {
        if (processId <= 0)
        {
            return;
        }

        try
        {
            using var process = Process.GetProcessById(processId);
            await process.WaitForExitAsync().ConfigureAwait(false);
        }
        catch (ArgumentException)
        {
            // The process already exited.
        }
    }

    private static void ValidatePlan(UpdateApplyPlan plan)
    {
        if (plan.SchemaVersion != UpdateProtocol.SchemaVersion
            || !UpdateVersion.TryParse(plan.CurrentVersion, out _)
            || !UpdateVersion.TryParse(plan.TargetVersion, out _)
            || !Guid.TryParse(plan.HealthToken, out _)
            || string.IsNullOrWhiteSpace(plan.InstallRoot)
            || string.IsNullOrWhiteSpace(plan.PackagePath)
            || string.IsNullOrWhiteSpace(plan.TargetManifestPath))
        {
            throw new InvalidDataException("The update plan is invalid.");
        }
    }

    private static void ValidateDescriptor(UpdateApplyPlan plan, ReleaseDescriptor descriptor)
    {
        UpdateValidation.ValidateReleaseDescriptor(descriptor, plan.CurrentVersion);
        if (!string.Equals(descriptor.Version, plan.TargetVersion, StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                "The release descriptor does not match the update plan.");
        }
    }

    private static void ValidateSelectedPackage(UpdateApplyPlan plan,
        ReleaseDescriptor descriptor, UpdateArtifact artifact)
    {
        if (plan.PackageKind == UpdatePackageKind.Full)
        {
            if (!string.Equals(artifact.Id, descriptor.FullPackage.Id,
                    StringComparison.Ordinal))
            {
                throw new InvalidDataException("The update plan has an invalid full package.");
            }
            return;
        }

        var delta = descriptor.Deltas.FirstOrDefault(value =>
            string.Equals(value.Id, artifact.Id, StringComparison.Ordinal));
        if (delta is null
            || !string.Equals(delta.BaseVersion, plan.CurrentVersion, StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                "The update plan does not contain a direct delta for the installed version.");
        }
    }

    private static void TryTerminate(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(true);
                process.WaitForExit(5000);
            }
        }
        catch (Exception exception)
        {
            BootstrapperLog.Write(
                $"Unable to terminate failed update process: {exception.Message}");
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception exception)
        {
            BootstrapperLog.Write($"Unable to delete update marker {path}: {exception.Message}");
        }
    }
}

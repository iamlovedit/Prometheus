using System.Diagnostics;
using System.Text.Json;
using Prometheus.Update;

namespace Prometheus.Updater;

internal static class Bootstrapper
{
    private static readonly TimeSpan HealthTimeout = TimeSpan.FromSeconds(60);

    public static Task<int> ApplyAsync(string planPath)
    {
        return ApplyAsync(planPath, null);
    }

    internal static async Task<int> ApplyAsync(string planPath, BootstrapperTestHooks? hooks)
    {
        var plan = JsonSerializer.Deserialize(await File.ReadAllBytesAsync(planPath),
            UpdateJsonContext.Default.UpdateApplyPlan)
            ?? throw new InvalidDataException("The update plan is empty.");
        UpdateValidation.ValidateApplyPlan(plan);
        await WaitForParentExitAsync(plan.ParentProcessId).ConfigureAwait(false);

        var installRoot = Path.GetFullPath(plan.InstallRoot).TrimEnd(
            Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var backupRoot = UpdateInstaller.GetBackupRoot(installRoot);
        RecoverInterruptedSwap(installRoot, backupRoot);
        if (!Directory.Exists(installRoot))
        {
            throw new DirectoryNotFoundException(
                $"The Prometheus installation directory was not found: {installRoot}");
        }

        string? stagingRoot = null;
        Process? process = null;
        var switched = false;
        try
        {
            stagingRoot = await UpdateInstaller.PrepareStagingAsync(plan,
                hooks?.ValidateDesktopVersion).ConfigureAwait(false);
            UpdateInstaller.TryDeleteDirectory(backupRoot);
            Directory.Move(installRoot, backupRoot);
            switched = true;
            Directory.Move(stagingRoot, installRoot);

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

            TryDelete(markerPath);
            TryDelete(plan.PackagePath);
            TryDelete(planPath);
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
            RollBackInstallation(installRoot, backupRoot);
            using var rollbackProcess = StartDesktop(installRoot, plan.CurrentVersion, null,
                hooks);
            BootstrapperLog.Write(
                $"Update to {plan.TargetVersion} was rolled back: {exception}");
            return 2;
        }
        catch (Exception exception)
        {
            if (Directory.Exists(installRoot))
            {
                using var currentProcess = StartDesktop(installRoot, plan.CurrentVersion,
                    null, hooks);
            }
            BootstrapperLog.Write(
                $"Update to {plan.TargetVersion} failed before switching versions: {exception}");
            return 1;
        }
        finally
        {
            process?.Dispose();
            if (stagingRoot is not null)
            {
                UpdateInstaller.TryDeleteDirectory(stagingRoot);
            }
        }
    }

    private static void RecoverInterruptedSwap(string installRoot, string backupRoot)
    {
        if (!Directory.Exists(installRoot) && Directory.Exists(backupRoot))
        {
            Directory.Move(backupRoot, installRoot);
            BootstrapperLog.Write(
                "Recovered an interrupted update by restoring the rollback directory.");
        }
    }

    private static void RollBackInstallation(string installRoot, string backupRoot)
    {
        var failedRoot = installRoot + ".failed";
        UpdateInstaller.TryDeleteDirectory(failedRoot);
        if (Directory.Exists(installRoot))
        {
            Directory.Move(installRoot, failedRoot);
        }
        if (!Directory.Exists(backupRoot))
        {
            throw new DirectoryNotFoundException(
                "The rollback directory is missing after an update failure.");
        }
        Directory.Move(backupRoot, installRoot);
        UpdateInstaller.TryDeleteDirectory(failedRoot);
    }

    private static Process StartDesktop(string installRoot, string version, string? healthToken,
        BootstrapperTestHooks? hooks)
    {
        if (hooks?.StartDesktop is not null)
        {
            return hooks.StartDesktop(installRoot, version, healthToken);
        }

        var executable = Path.Combine(installRoot, UpdateProtocol.DesktopExecutableName);
        if (!File.Exists(executable))
        {
            throw new FileNotFoundException(
                "The Prometheus desktop executable was not found.", executable);
        }

        var startInfo = new ProcessStartInfo(executable)
        {
            WorkingDirectory = installRoot,
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
            BootstrapperLog.Write($"Unable to delete update file {path}: {exception.Message}");
        }
    }
}

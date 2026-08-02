using Microsoft.Win32;
using Newtonsoft.Json.Linq;
using Prometheus.Services.Interfaces.Client;
using Serilog;
using System.Diagnostics;

namespace Prometheus.Services.Client
{
    public sealed class LeagueClientLauncher : ILeagueClientLauncher
    {
        private const string RiotClientExecutableName = "RiotClientServices.exe";
        private const string TencentClientRelativePath = @"TCLS\Client.exe";
        private const string WeGameLauncherRelativePath =
            @"WeGameLauncher\launcher.exe";
        private const string LeagueProductArgument =
            "--launch-product=league_of_legends";
        private const string LivePatchlineArgument = "--launch-patchline=live";

        private static readonly (string KeyName, string ValueName)[]
            TencentInstallRegistryValues =
            [
                (@"HKEY_CURRENT_USER\Software\Tencent\LOL", "InstallPath"),
                (@"HKEY_LOCAL_MACHINE\SOFTWARE\WOW6432Node\Tencent\LOL",
                    "InstallPath"),
                (@"HKEY_LOCAL_MACHINE\SOFTWARE\WOW6432Node\Tencent\LOL_LCU",
                    "InstallPath"),
                (@"HKEY_LOCAL_MACHINE\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\英雄联盟",
                    "InstallSource")
            ];

        private readonly SemaphoreSlim _launchGate = new(1, 1);
        private readonly Func<bool> _isLeagueClientRunning;
        private readonly Func<LeagueClientLaunchTarget> _resolveLaunchTarget;
        private readonly Func<ProcessStartInfo, Process> _startProcess;
        private readonly ILogger _logger;

        public LeagueClientLauncher()
            : this(
                IsLeagueClientRunningCore,
                ResolveLaunchTarget,
                startInfo => Process.Start(startInfo),
                Log.ForContext<LeagueClientLauncher>())
        {
        }

        internal LeagueClientLauncher(
            Func<bool> isLeagueClientRunning,
            Func<LeagueClientLaunchTarget> resolveLaunchTarget,
            Func<ProcessStartInfo, Process> startProcess,
            ILogger logger)
        {
            _isLeagueClientRunning = isLeagueClientRunning ??
                throw new ArgumentNullException(nameof(isLeagueClientRunning));
            _resolveLaunchTarget = resolveLaunchTarget ??
                throw new ArgumentNullException(nameof(resolveLaunchTarget));
            _startProcess = startProcess ??
                throw new ArgumentNullException(nameof(startProcess));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public bool IsLeagueClientRunning()
        {
            try
            {
                return _isLeagueClientRunning();
            }
            catch (Exception exception)
            {
                _logger.Debug(exception,
                    "Unable to inspect the League client process state");
                return false;
            }
        }

        public async Task<LeagueClientLaunchStatus> LaunchAsync(
            CancellationToken cancellationToken = default)
        {
            await _launchGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                if (IsLeagueClientRunning())
                {
                    return LeagueClientLaunchStatus.AlreadyRunning;
                }

                var launchTarget = _resolveLaunchTarget();
                if (launchTarget is null)
                {
                    _logger.Debug(
                        "Unable to launch League of Legends because no supported launcher was found");
                    return LeagueClientLaunchStatus.LauncherNotFound;
                }

                if (launchTarget.RequiresExternalLauncher)
                {
                    _logger.Warning(
                        "The installed League client requires its regional launcher; automatic startup was not attempted");
                    return LeagueClientLaunchStatus.ExternalLauncherRequired;
                }

                var startInfo = CreateStartInfo(launchTarget);
                var process = _startProcess(startInfo);
                if (process is null)
                {
                    _logger.Error(
                        "The game launcher did not return a process after the League launch request");
                    return LeagueClientLaunchStatus.Failed;
                }

                process.Dispose();
                _logger.Debug(
                    "Requested League of Legends startup through the installed launcher");
                return LeagueClientLaunchStatus.Started;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                _logger.Error(exception,
                    "Unable to launch League of Legends through the installed launcher");
                return LeagueClientLaunchStatus.Failed;
            }
            finally
            {
                _launchGate.Release();
            }
        }

        internal static ProcessStartInfo CreateStartInfo(
            LeagueClientLaunchTarget launchTarget)
        {
            if (launchTarget.RequiresExternalLauncher)
            {
                throw new InvalidOperationException(
                    "An external-launcher requirement cannot be converted to process start information.");
            }

            var startInfo = new ProcessStartInfo(launchTarget.ExecutablePath)
            {
                UseShellExecute = launchTarget.UseShellExecute,
                WorkingDirectory = launchTarget.WorkingDirectory
            };
            foreach (var argument in launchTarget.Arguments)
            {
                startInfo.ArgumentList.Add(argument);
            }

            return startInfo;
        }

        internal static LeagueClientLaunchTarget CreateLaunchTarget(
            string riotClientExecutablePath,
            Func<string, bool> fileExists)
        {
            if (string.IsNullOrWhiteSpace(riotClientExecutablePath) || fileExists is null)
            {
                return null;
            }

            var riotClientDirectory = Path.GetDirectoryName(riotClientExecutablePath);
            var installDirectory = string.IsNullOrWhiteSpace(riotClientDirectory)
                ? null
                : Directory.GetParent(riotClientDirectory)?.FullName;
            if (!string.IsNullOrWhiteSpace(installDirectory))
            {
                var tencentTarget = CreateTencentLaunchTarget(
                    installDirectory, fileExists);
                if (tencentTarget is not null)
                {
                    return tencentTarget;
                }

                var weGameLauncherPath = Path.Combine(
                    installDirectory, WeGameLauncherRelativePath);
                if (fileExists(weGameLauncherPath))
                {
                    return LeagueClientLaunchTarget.ExternalLauncherRequired();
                }
            }

            if (!fileExists(riotClientExecutablePath))
            {
                return null;
            }

            return new LeagueClientLaunchTarget(
                riotClientExecutablePath,
                [LeagueProductArgument, LivePatchlineArgument],
                useShellExecute: false);
        }

        internal static LeagueClientLaunchTarget CreateTencentLaunchTarget(
            string installDirectory,
            Func<string, bool> fileExists)
        {
            if (string.IsNullOrWhiteSpace(installDirectory) || fileExists is null)
            {
                return null;
            }

            var tencentClientPath = Path.Combine(
                installDirectory, TencentClientRelativePath);
            if (!fileExists(tencentClientPath))
            {
                return null;
            }

            return new LeagueClientLaunchTarget(
                tencentClientPath,
                Array.Empty<string>(),
                useShellExecute: true,
                workingDirectory: installDirectory);
        }

        internal static string ReadRiotClientExecutable(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                return null;
            }

            try
            {
                var installs = JObject.Parse(json);
                foreach (var key in new[] { "rc_live", "rc_default" })
                {
                    var value = installs.GetValue(
                        key, StringComparison.OrdinalIgnoreCase)?.Value<string>();
                    if (!string.IsNullOrWhiteSpace(value))
                    {
                        return value;
                    }
                }

                foreach (var containerName in new[] { "associated_client", "patchlines" })
                {
                    if (installs.GetValue(containerName,
                            StringComparison.OrdinalIgnoreCase) is not JObject container)
                    {
                        continue;
                    }

                    var value = container.Properties()
                        .Select(property => property.Value.Value<string>())
                        .FirstOrDefault(path => !string.IsNullOrWhiteSpace(path));
                    if (!string.IsNullOrWhiteSpace(value))
                    {
                        return value;
                    }
                }
            }
            catch
            {
            }

            return null;
        }

        private static bool IsLeagueClientRunningCore()
        {
            return IsProcessRunning("LeagueClient") ||
                   IsProcessRunning("LeagueClientUx");
        }

        private static bool IsProcessRunning(string processName)
        {
            foreach (var process in Process.GetProcessesByName(processName))
            {
                using (process)
                {
                    try
                    {
                        if (!process.HasExited)
                        {
                            return true;
                        }
                    }
                    catch (InvalidOperationException)
                    {
                    }
                }
            }

            return false;
        }

        private static LeagueClientLaunchTarget ResolveLaunchTarget()
        {
            foreach (var candidate in GetTencentInstallDirectoryCandidates())
            {
                try
                {
                    var normalized = NormalizeInstallDirectoryCandidate(candidate);
                    var launchTarget = CreateTencentLaunchTarget(
                        normalized, File.Exists);
                    if (launchTarget is not null)
                    {
                        return launchTarget;
                    }
                }
                catch (ArgumentException)
                {
                }
                catch (NotSupportedException)
                {
                }
                catch (PathTooLongException)
                {
                }
            }

            foreach (var candidate in GetRiotClientExecutableCandidates())
            {
                try
                {
                    var normalized = NormalizeExecutableCandidate(candidate);
                    var launchTarget = CreateLaunchTarget(normalized, File.Exists);
                    if (launchTarget is not null)
                    {
                        return launchTarget;
                    }
                }
                catch (ArgumentException)
                {
                }
                catch (NotSupportedException)
                {
                }
                catch (PathTooLongException)
                {
                }
            }

            return null;
        }

        private static IEnumerable<string> GetTencentInstallDirectoryCandidates()
        {
            foreach (var (keyName, valueName) in TencentInstallRegistryValues)
            {
                string configuredPath = null;
                try
                {
                    configuredPath = Registry.GetValue(
                        keyName, valueName, null) as string;
                }
                catch (Exception exception) when (exception is ArgumentException or
                    IOException or UnauthorizedAccessException or
                    System.Security.SecurityException)
                {
                }

                if (!string.IsNullOrWhiteSpace(configuredPath))
                {
                    yield return configuredPath;
                }
            }
        }

        private static IEnumerable<string> GetRiotClientExecutableCandidates()
        {
            var installsPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                "Riot Games",
                "RiotClientInstalls.json");
            if (File.Exists(installsPath))
            {
                string configuredPath = null;
                try
                {
                    configuredPath = ReadRiotClientExecutable(
                        File.ReadAllText(installsPath));
                }
                catch (IOException)
                {
                }
                catch (UnauthorizedAccessException)
                {
                }

                if (!string.IsNullOrWhiteSpace(configuredPath))
                {
                    yield return configuredPath;
                }
            }

            var runningPath = GetRunningRiotClientExecutable();
            if (!string.IsNullOrWhiteSpace(runningPath))
            {
                yield return runningPath;
            }

            var systemRoot = Path.GetPathRoot(Environment.SystemDirectory);
            if (!string.IsNullOrWhiteSpace(systemRoot))
            {
                yield return Path.Combine(systemRoot, "Riot Games", "Riot Client",
                    RiotClientExecutableName);
            }

            var programFiles = Environment.GetFolderPath(
                Environment.SpecialFolder.ProgramFiles);
            if (!string.IsNullOrWhiteSpace(programFiles))
            {
                yield return Path.Combine(programFiles, "Riot Games", "Riot Client",
                    RiotClientExecutableName);
            }

            var programFilesX86 = Environment.GetFolderPath(
                Environment.SpecialFolder.ProgramFilesX86);
            if (!string.IsNullOrWhiteSpace(programFilesX86))
            {
                yield return Path.Combine(programFilesX86, "Riot Games", "Riot Client",
                    RiotClientExecutableName);
            }
        }

        private static string GetRunningRiotClientExecutable()
        {
            foreach (var process in Process.GetProcessesByName("RiotClientServices"))
            {
                using (process)
                {
                    try
                    {
                        var path = process.MainModule?.FileName;
                        if (!string.IsNullOrWhiteSpace(path))
                        {
                            return path;
                        }
                    }
                    catch (InvalidOperationException)
                    {
                    }
                    catch (System.ComponentModel.Win32Exception)
                    {
                    }
                }
            }

            return null;
        }

        private static string NormalizeExecutableCandidate(string candidate)
        {
            if (string.IsNullOrWhiteSpace(candidate))
            {
                return null;
            }

            var path = Environment.ExpandEnvironmentVariables(candidate.Trim().Trim('"'))
                .Replace('/', Path.DirectorySeparatorChar);
            if (Directory.Exists(path))
            {
                path = Path.Combine(path, RiotClientExecutableName);
            }

            return Path.GetFullPath(path);
        }

        private static string NormalizeInstallDirectoryCandidate(string candidate)
        {
            if (string.IsNullOrWhiteSpace(candidate))
            {
                return null;
            }

            var path = Environment.ExpandEnvironmentVariables(
                    candidate.Trim().Trim('"'))
                .Replace('/', Path.DirectorySeparatorChar);
            return Path.GetFullPath(path);
        }
    }

    internal sealed class LeagueClientLaunchTarget
    {
        private LeagueClientLaunchTarget()
        {
            RequiresExternalLauncher = true;
            Arguments = Array.Empty<string>();
            WorkingDirectory = string.Empty;
        }

        internal LeagueClientLaunchTarget(
            string executablePath,
            IReadOnlyList<string> arguments,
            bool useShellExecute,
            string workingDirectory = null)
        {
            ExecutablePath = executablePath ??
                throw new ArgumentNullException(nameof(executablePath));
            Arguments = arguments ?? throw new ArgumentNullException(nameof(arguments));
            UseShellExecute = useShellExecute;
            WorkingDirectory = string.IsNullOrWhiteSpace(workingDirectory)
                ? Path.GetDirectoryName(executablePath) ?? string.Empty
                : workingDirectory;
        }

        internal bool RequiresExternalLauncher { get; }

        internal string ExecutablePath { get; }

        internal IReadOnlyList<string> Arguments { get; }

        internal bool UseShellExecute { get; }

        internal string WorkingDirectory { get; }

        internal static LeagueClientLaunchTarget ExternalLauncherRequired()
        {
            return new LeagueClientLaunchTarget();
        }
    }
}

using Prometheus.Services.Interfaces.Updates;
using Prometheus.Update;
using System.Diagnostics;
using System.Reflection;
using System.Text.Json;

namespace Prometheus;

internal static class UpdateRuntime
{
    public static UpdateServiceOptions CreateOptions()
    {
        var entryAssembly = Assembly.GetEntryAssembly();
        var apiBaseUrl = entryAssembly?
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(attribute => attribute.Key == "UpdateApiBaseUrl")?.Value;
        apiBaseUrl = Environment.GetEnvironmentVariable("PROMETHEUS_UPDATE_API_URL")
            ?? apiBaseUrl
            ?? string.Empty;

        var version = entryAssembly?
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion.Split('+')[0]
            ?? "1.0.0";
        var installRoot = ResolveInstallRoot();
        return new UpdateServiceOptions
        {
            ApiBaseUrl = apiBaseUrl,
            InstallRoot = installRoot,
            CurrentVersion = version,
            BootstrapperVersion = ReadBootstrapperVersion(installRoot)
        };
    }

    public static void MarkHealthReady()
    {
        var args = Environment.GetCommandLineArgs();
        for (var index = 1; index < args.Length - 1; index++)
        {
            if (!string.Equals(args[index], "--health-token", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var path = UpdatePaths.GetHealthMarkerPath(args[index + 1]);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, DateTimeOffset.UtcNow.ToString("O"));
            return;
        }
    }

    private static string ResolveInstallRoot()
    {
        var current = new DirectoryInfo(Path.GetFullPath(AppContext.BaseDirectory));
        if (UpdateVersion.TryParse(current.Name, out _)
            && string.Equals(current.Parent?.Name, "versions", StringComparison.OrdinalIgnoreCase)
            && current.Parent.Parent is not null)
        {
            return current.Parent.Parent.FullName;
        }

        return current.FullName;
    }

    private static string ReadBootstrapperVersion(string installRoot)
    {
        try
        {
            var executable = Path.Combine(installRoot,
                UpdateProtocol.BootstrapperExecutableName);
            if (File.Exists(executable))
            {
                var productVersion = FileVersionInfo.GetVersionInfo(executable).ProductVersion?
                    .Split('+')[0];
                if (UpdateVersion.TryParse(productVersion, out var parsed))
                {
                    return parsed.ToString();
                }
            }

            var path = Path.Combine(installRoot, "current.json");
            if (!File.Exists(path))
            {
                return "1.0.0";
            }

            return JsonSerializer.Deserialize(File.ReadAllBytes(path),
                       UpdateJsonContext.Default.BootstrapperState)?.BootstrapperVersion
                   ?? "1.0.0";
        }
        catch
        {
            return "1.0.0";
        }
    }
}

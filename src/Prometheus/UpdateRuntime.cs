#nullable enable

using Prometheus.Services.Interfaces.Updates;
using Prometheus.Update;
using System.Reflection;

namespace Prometheus;

internal static class UpdateRuntime
{
    public static UpdateServiceOptions CreateOptions()
    {
        var entryAssembly = Assembly.GetEntryAssembly();
        var metadata = entryAssembly?.GetCustomAttributes<AssemblyMetadataAttribute>()
            .ToDictionary(attribute => attribute.Key, attribute => attribute.Value,
                StringComparer.OrdinalIgnoreCase)
            ?? new Dictionary<string, string?>();
        var version = entryAssembly?
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion.Split('+')[0]
            ?? "1.0.0";
        var installRoot = Path.GetFullPath(AppContext.BaseDirectory);
        return new UpdateServiceOptions
        {
            GitHubOwner = GetMetadata(metadata, "GitHubRepositoryOwner"),
            GitHubRepository = GetMetadata(metadata, "GitHubRepositoryName"),
            GitHubApiBaseUrl = "https://api.github.com",
            InstallRoot = installRoot,
            CurrentVersion = version,
            UpdaterPath = Path.Combine(installRoot, UpdateProtocol.UpdaterExecutableName)
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

    private static string GetMetadata(IReadOnlyDictionary<string, string?> metadata, string key)
    {
        return metadata.TryGetValue(key, out var value) ? value ?? string.Empty : string.Empty;
    }
}

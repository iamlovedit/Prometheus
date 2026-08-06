using System.Text.Json.Serialization;

namespace Prometheus.Update;

public static class UpdateProtocol
{
    public const int SchemaVersion = 1;
    public const string WindowsX64Rid = "win-x64";
    public const string DesktopExecutableName = "Prometheus.Desktop.exe";
    public const string UpdaterExecutableName = "Prometheus.Updater.exe";
    public const string GitHubApiAccept = "application/vnd.github+json";
    public const string GitHubApiVersion = "2022-11-28";
}

public sealed class GitHubRelease
{
    [JsonPropertyName("tag_name")]
    public string TagName { get; set; } = string.Empty;

    public string? Name { get; set; }
    public string? Body { get; set; }
    public bool Draft { get; set; }
    public bool Prerelease { get; set; }
    public List<GitHubReleaseAsset> Assets { get; set; } = [];
}

public sealed class GitHubReleaseAsset
{
    public string Name { get; set; } = string.Empty;
    public long Size { get; set; }

    [JsonPropertyName("browser_download_url")]
    public Uri BrowserDownloadUrl { get; set; } = null!;
}

public sealed class GitHubReleaseSelection
{
    public string Version { get; init; } = string.Empty;
    public GitHubRelease Release { get; init; } = new();
    public GitHubReleaseAsset Package { get; init; } = new();
    public GitHubReleaseAsset Checksum { get; init; } = new();
}

public sealed class UpdateDownloadMetadata
{
    public int SchemaVersion { get; set; } = UpdateProtocol.SchemaVersion;
    public string Version { get; set; } = string.Empty;
    public string AssetName { get; set; } = string.Empty;
    public long AssetSize { get; set; }
}

public sealed class UpdateApplyPlan
{
    public int SchemaVersion { get; set; } = UpdateProtocol.SchemaVersion;
    public string InstallRoot { get; set; } = string.Empty;
    public string CurrentVersion { get; set; } = string.Empty;
    public string TargetVersion { get; set; } = string.Empty;
    public int ParentProcessId { get; set; }
    public string HealthToken { get; set; } = string.Empty;
    public string PackagePath { get; set; } = string.Empty;
    public long PackageSize { get; set; }
    public string PackageSha256 { get; set; } = string.Empty;
}

[JsonSerializable(typeof(GitHubRelease))]
[JsonSerializable(typeof(UpdateDownloadMetadata))]
[JsonSerializable(typeof(UpdateApplyPlan))]
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    WriteIndented = true, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
public partial class UpdateJsonContext : JsonSerializerContext
{
}

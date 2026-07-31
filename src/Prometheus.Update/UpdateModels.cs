using System.Text.Json.Serialization;

namespace Prometheus.Update;

public static class UpdateProtocol
{
    public const int SchemaVersion = 1;
    public const string StableChannel = "stable";
    public const string WindowsX64Rid = "win-x64";
    public const string DesktopExecutableName = "Prometheus.Desktop.exe";
    public const string BootstrapperExecutableName = "Prometheus.exe";
    public const string InstalledManifestFileName = ".release-manifest.json";
}

public sealed class SignedEnvelope
{
    public string Payload { get; set; } = string.Empty;
    public string Signature { get; set; } = string.Empty;
}

public sealed class UpdateArtifact
{
    public string Id { get; set; } = string.Empty;
    public string ObjectKey { get; set; } = string.Empty;
    public long Size { get; set; }
    public string Sha256 { get; set; } = string.Empty;
}

public sealed class DeltaArtifact
{
    public string Id { get; set; } = string.Empty;
    public string BaseVersion { get; set; } = string.Empty;
    public string ObjectKey { get; set; } = string.Empty;
    public long Size { get; set; }
    public string Sha256 { get; set; } = string.Empty;
}

public sealed class ReleaseDescriptor
{
    public int SchemaVersion { get; set; } = UpdateProtocol.SchemaVersion;
    public string Channel { get; set; } = UpdateProtocol.StableChannel;
    public string Rid { get; set; } = UpdateProtocol.WindowsX64Rid;
    public string Version { get; set; } = string.Empty;
    public string MinimumSupportedVersion { get; set; } = "0.0.0";
    public string MinimumBootstrapperVersion { get; set; } = "1.0.0";
    public string BootstrapperVersion { get; set; } = "1.0.0";
    public int RolloutPercentage { get; set; } = 100;
    public DateTimeOffset PublishedAt { get; set; }
    public Dictionary<string, string> ReleaseNotes { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public UpdateArtifact TargetManifest { get; set; } = new();
    public UpdateArtifact FullPackage { get; set; } = new();
    public UpdateArtifact PortablePackage { get; set; } = new();
    public UpdateArtifact? Bootstrapper { get; set; }
    public List<DeltaArtifact> Deltas { get; set; } = [];
}

public sealed class ReleaseManifest
{
    public int SchemaVersion { get; set; } = UpdateProtocol.SchemaVersion;
    public string Version { get; set; } = string.Empty;
    public List<ReleaseFileEntry> Files { get; set; } = [];
}

public sealed class ReleaseFileEntry
{
    public string Path { get; set; } = string.Empty;
    public long Size { get; set; }
    public string Sha256 { get; set; } = string.Empty;
}

public sealed class ChannelIndex
{
    public int SchemaVersion { get; set; } = UpdateProtocol.SchemaVersion;
    public string Channel { get; set; } = UpdateProtocol.StableChannel;
    public string Rid { get; set; } = UpdateProtocol.WindowsX64Rid;
    public List<ChannelRelease> Releases { get; set; } = [];
}

public sealed class ChannelRelease
{
    public string Version { get; set; } = string.Empty;
    public string ReleaseObjectKey { get; set; } = string.Empty;
    public string ManifestObjectKey { get; set; } = string.Empty;
    public DateTimeOffset PublishedAt { get; set; }
}

public sealed class UpdateApiResponse
{
    public SignedEnvelope Release { get; set; } = new();
    public string SelectedArtifactId { get; set; } = string.Empty;
    public Uri ManifestUrl { get; set; } = null!;
    public Uri PackageUrl { get; set; } = null!;
    public Uri FullPackageUrl { get; set; } = null!;
    public Uri? BootstrapperUrl { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
}

public sealed class BootstrapperState
{
    public int SchemaVersion { get; set; } = UpdateProtocol.SchemaVersion;
    public string CurrentVersion { get; set; } = string.Empty;
    public string? RollbackVersion { get; set; }
    public string? PendingHealthToken { get; set; }
    public string BootstrapperVersion { get; set; } = "1.0.0";
}

public enum UpdatePackageKind
{
    Full,
    Delta
}

public sealed class UpdateApplyPlan
{
    public int SchemaVersion { get; set; } = UpdateProtocol.SchemaVersion;
    public string InstallRoot { get; set; } = string.Empty;
    public string CurrentVersion { get; set; } = string.Empty;
    public string TargetVersion { get; set; } = string.Empty;
    public int ParentProcessId { get; set; }
    public string HealthToken { get; set; } = string.Empty;
    public UpdatePackageKind PackageKind { get; set; }
    public string SelectedArtifactId { get; set; } = string.Empty;
    public string PackagePath { get; set; } = string.Empty;
    public string TargetManifestPath { get; set; } = string.Empty;
    public string? BootstrapperPath { get; set; }
    public SignedEnvelope Release { get; set; } = new();
}

public sealed class UploadMap
{
    public List<UploadMapEntry> Files { get; set; } = [];
    public UploadMapEntry ChannelPointer { get; set; } = new();
}

public sealed class UploadMapEntry
{
    public string LocalPath { get; set; } = string.Empty;
    public string ObjectKey { get; set; } = string.Empty;
    public string ContentType { get; set; } = "application/octet-stream";
}

[JsonSerializable(typeof(SignedEnvelope))]
[JsonSerializable(typeof(ReleaseDescriptor))]
[JsonSerializable(typeof(ReleaseManifest))]
[JsonSerializable(typeof(ChannelIndex))]
[JsonSerializable(typeof(UpdateApiResponse))]
[JsonSerializable(typeof(BootstrapperState))]
[JsonSerializable(typeof(UpdateApplyPlan))]
[JsonSerializable(typeof(UploadMap))]
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    WriteIndented = true, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
public partial class UpdateJsonContext : JsonSerializerContext
{
}

namespace Prometheus.ReleaseTool;

internal sealed class ReleaseOptions
{
    public string Version { get; private init; } = string.Empty;
    public string GitTag { get; private init; } = string.Empty;
    public string PublishDirectory { get; private init; } = string.Empty;
    public string BootstrapperPath { get; private init; } = string.Empty;
    public string OutputDirectory { get; private init; } = string.Empty;
    public string PrivateKeyPath { get; private init; } = string.Empty;
    public string RepositoryRoot { get; private init; } = string.Empty;
    public string AccountId { get; private init; } = string.Empty;
    public string AccessKey { get; private init; } = string.Empty;
    public string SecretKey { get; private init; } = string.Empty;
    public string Bucket { get; private init; } = string.Empty;
    public string MinimumSupportedVersion { get; private init; } = "0.0.0";
    public string MinimumBootstrapperVersion { get; private init; } = "1.0.0";
    public string BootstrapperVersion { get; private init; } = string.Empty;
    public string NotesZh { get; private init; } = string.Empty;
    public string NotesEn { get; private init; } = string.Empty;

    public static ReleaseOptions Parse(string[] args)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < args.Length; index += 2)
        {
            if (index + 1 >= args.Length || !args[index].StartsWith("--", StringComparison.Ordinal))
            {
                throw new ArgumentException("ReleaseTool arguments must use --name value pairs.");
            }
            values[args[index]] = args[index + 1];
        }

        string Required(string key)
        {
            return values.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value)
                ? value
                : throw new ArgumentException($"Missing required option {key}.");
        }

        var version = Required("--version");
        return new ReleaseOptions
        {
            Version = version,
            GitTag = Required("--git-tag"),
            PublishDirectory = Path.GetFullPath(Required("--publish-dir")),
            BootstrapperPath = Path.GetFullPath(Required("--bootstrapper")),
            OutputDirectory = Path.GetFullPath(Required("--output")),
            PrivateKeyPath = Path.GetFullPath(Required("--private-key")),
            RepositoryRoot = Path.GetFullPath(Required("--repository-root")),
            AccountId = Required("--account-id"),
            AccessKey = Required("--access-key"),
            SecretKey = Required("--secret-key"),
            Bucket = Required("--bucket"),
            MinimumSupportedVersion = values.GetValueOrDefault("--minimum-supported") ?? "0.0.0",
            MinimumBootstrapperVersion = values.GetValueOrDefault("--minimum-bootstrapper")
                ?? "1.0.0",
            BootstrapperVersion = values.GetValueOrDefault("--bootstrapper-version") ?? version,
            NotesZh = values.GetValueOrDefault("--notes-zh") ?? string.Empty,
            NotesEn = values.GetValueOrDefault("--notes-en") ?? string.Empty
        };
    }
}

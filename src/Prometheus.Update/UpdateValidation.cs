namespace Prometheus.Update;

public static class UpdateValidation
{
    private const int MaximumChecksumFileSize = 16 * 1024;

    public static GitHubReleaseSelection ValidateGitHubRelease(GitHubRelease release,
        string owner, string repository)
    {
        ArgumentNullException.ThrowIfNull(release);
        ValidateRepositoryCoordinate(owner, nameof(owner));
        ValidateRepositoryCoordinate(repository, nameof(repository));

        if (release.Draft || release.Prerelease
            || !TryParseTag(release.TagName, out var version)
            || release.Assets is null)
        {
            throw new InvalidDataException("The latest GitHub Release is not a stable Prometheus release.");
        }

        var packageName = $"Prometheus-{version}-{UpdateProtocol.WindowsX64Rid}.zip";
        var checksumName = packageName + ".sha256";
        var package = ResolveSingleAsset(release.Assets, packageName);
        var checksum = ResolveOptionalSingleAsset(release.Assets, checksumName);
        var packageDigestSha256 = ParseGitHubAssetSha256(package.Digest);
        if (checksum is null && packageDigestSha256 is null)
        {
            throw new InvalidDataException(
                "The GitHub Release does not provide SHA-256 for the update package.");
        }

        if (package.Size <= 0 || checksum is not null
                && (checksum.Size <= 0 || checksum.Size > MaximumChecksumFileSize))
        {
            throw new InvalidDataException("The GitHub Release contains invalid update asset sizes.");
        }

        ValidateReleaseAssetUrl(package.BrowserDownloadUrl, owner, repository,
            release.TagName, packageName);
        if (checksum is not null)
        {
            ValidateReleaseAssetUrl(checksum.BrowserDownloadUrl, owner, repository,
                release.TagName, checksumName);
        }
        return new GitHubReleaseSelection
        {
            Version = version.ToString(),
            Release = release,
            Package = package,
            Checksum = checksum,
            PackageDigestSha256 = packageDigestSha256
        };
    }

    public static string ParseSha256File(string content, string expectedFileName)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentException.ThrowIfNullOrWhiteSpace(expectedFileName);
        if (content.Length == 0 || content.Length > MaximumChecksumFileSize
            || content.IndexOf('\0') >= 0)
        {
            throw new InvalidDataException("The update checksum file is invalid.");
        }

        var value = content.EndsWith("\r\n", StringComparison.Ordinal)
            ? content[..^2]
            : content.EndsWith('\n') ? content[..^1] : content;
        if (value.Length < 64 || value.Contains('\r') || value.Contains('\n'))
        {
            throw new InvalidDataException("The update checksum file is invalid.");
        }

        var hash = value[..64];
        if (!IsSha256(hash))
        {
            throw new InvalidDataException("The update checksum file does not contain SHA-256.");
        }

        if (value.Length > 64)
        {
            var separatorLength = 0;
            while (64 + separatorLength < value.Length
                   && value[64 + separatorLength] is ' ' or '\t')
            {
                separatorLength++;
            }
            if (separatorLength == 0 || 64 + separatorLength >= value.Length)
            {
                throw new InvalidDataException("The update checksum file is invalid.");
            }

            var fileName = value[(64 + separatorLength)..];
            if (fileName.StartsWith('*'))
            {
                fileName = fileName[1..];
            }
            if (!string.Equals(fileName, expectedFileName, StringComparison.Ordinal))
            {
                throw new InvalidDataException("The update checksum references a different asset.");
            }
        }

        return hash.ToLowerInvariant();
    }

    public static void ValidateApplyPlan(UpdateApplyPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        if (plan.SchemaVersion != UpdateProtocol.SchemaVersion
            || string.IsNullOrWhiteSpace(plan.InstallRoot)
            || string.IsNullOrWhiteSpace(plan.PackagePath)
            || plan.ParentProcessId < 0
            || plan.PackageSize <= 0
            || !IsSha256(plan.PackageSha256)
            || !Guid.TryParse(plan.HealthToken, out _)
            || !UpdateVersion.TryParse(plan.CurrentVersion, out var current)
            || !UpdateVersion.TryParse(plan.TargetVersion, out var target)
            || target.CompareTo(current) <= 0)
        {
            throw new InvalidDataException("The update apply plan is invalid.");
        }
    }

    public static bool TryParseTag(string? tag, out UpdateVersion version)
    {
        version = default;
        return tag is { Length: > 1 } && tag[0] == 'v'
            && UpdateVersion.TryParse(tag[1..], out version);
    }

    public static bool IsSha256(string? value)
    {
        return value is { Length: 64 }
               && value.All(character => character is >= '0' and <= '9'
                   or >= 'a' and <= 'f' or >= 'A' and <= 'F');
    }

    private static GitHubReleaseAsset ResolveSingleAsset(
        IEnumerable<GitHubReleaseAsset> assets, string expectedName)
    {
        var matches = assets.Where(asset => asset is not null
            && string.Equals(asset.Name, expectedName, StringComparison.Ordinal)).ToArray();
        if (matches.Length != 1)
        {
            throw new InvalidDataException(
                $"The GitHub Release must contain exactly one {expectedName} asset.");
        }
        return matches[0];
    }

    private static GitHubReleaseAsset? ResolveOptionalSingleAsset(
        IEnumerable<GitHubReleaseAsset> assets, string expectedName)
    {
        var matches = assets.Where(asset => asset is not null
            && string.Equals(asset.Name, expectedName, StringComparison.Ordinal)).ToArray();
        if (matches.Length > 1)
        {
            throw new InvalidDataException(
                $"The GitHub Release contains more than one {expectedName} asset.");
        }
        return matches.SingleOrDefault();
    }

    private static string? ParseGitHubAssetSha256(string? digest)
    {
        if (digest is null)
        {
            return null;
        }

        const string prefix = "sha256:";
        if (!digest.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                "The GitHub Release contains an unsupported Asset digest.");
        }

        var hash = digest[prefix.Length..];
        if (!IsSha256(hash))
        {
            throw new InvalidDataException(
                "The GitHub Release contains an invalid SHA-256 Asset digest.");
        }
        return hash.ToLowerInvariant();
    }

    private static void ValidateReleaseAssetUrl(Uri? uri, string owner, string repository,
        string tag, string assetName)
    {
        var expectedPath = $"/{owner}/{repository}/releases/download/{tag}/{assetName}";
        if (uri is null || !uri.IsAbsoluteUri
            || !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(uri.Host, "github.com", StringComparison.OrdinalIgnoreCase)
            || !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment)
            || !string.Equals(Uri.UnescapeDataString(uri.AbsolutePath), expectedPath,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("The GitHub Release contains an invalid asset URL.");
        }
    }

    private static void ValidateRepositoryCoordinate(string value, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        if (value.Any(character => !char.IsAsciiLetterOrDigit(character)
                && character is not '-' and not '_' and not '.'))
        {
            throw new ArgumentException("Invalid GitHub repository coordinate.", parameterName);
        }
    }
}

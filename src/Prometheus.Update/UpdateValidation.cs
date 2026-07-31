namespace Prometheus.Update;

public static class UpdateValidation
{
    public static void ValidateChannelIndex(ChannelIndex channel, bool allowEmpty = false)
    {
        ArgumentNullException.ThrowIfNull(channel);
        if (channel.SchemaVersion != UpdateProtocol.SchemaVersion
            || channel.Channel != UpdateProtocol.StableChannel
            || channel.Rid != UpdateProtocol.WindowsX64Rid
            || channel.Releases is null
            || !allowEmpty && channel.Releases.Count == 0)
        {
            throw new InvalidDataException("The stable channel pointer is invalid.");
        }

        var versions = new HashSet<string>(StringComparer.Ordinal);
        UpdateVersion? previousVersion = null;
        foreach (var release in channel.Releases)
        {
            ArgumentNullException.ThrowIfNull(release);
            var version = UpdateVersion.Parse(release.Version);
            var prefix = $"releases/{release.Version}/{UpdateProtocol.WindowsX64Rid}/";
            if (!versions.Add(release.Version)
                || previousVersion is not null
                && previousVersion.Value.CompareTo(version) <= 0
                || release.ReleaseObjectKey != $"{prefix}release.json"
                || release.ManifestObjectKey != $"{prefix}manifest.json"
                || release.PublishedAt == default)
            {
                throw new InvalidDataException("The stable channel contains an invalid release.");
            }
            previousVersion = version;
        }
    }

    public static void ValidateReleaseDescriptor(ReleaseDescriptor descriptor,
        string? currentVersion = null)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        var version = UpdateVersion.Parse(descriptor.Version);
        var minimumSupported = UpdateVersion.Parse(descriptor.MinimumSupportedVersion);
        var minimumBootstrapper = UpdateVersion.Parse(descriptor.MinimumBootstrapperVersion);
        var bootstrapperVersion = UpdateVersion.Parse(descriptor.BootstrapperVersion);
        if (descriptor.SchemaVersion != UpdateProtocol.SchemaVersion
            || descriptor.Channel != UpdateProtocol.StableChannel
            || descriptor.Rid != UpdateProtocol.WindowsX64Rid
            || descriptor.RolloutPercentage is < 0 or > 100
            || descriptor.PublishedAt == default
            || descriptor.ReleaseNotes is null
            || descriptor.Deltas is null
            || minimumSupported.CompareTo(version) > 0
            || minimumBootstrapper.CompareTo(bootstrapperVersion) > 0)
        {
            throw new InvalidDataException("The update release descriptor is invalid.");
        }

        if (currentVersion is not null
            && version.CompareTo(UpdateVersion.Parse(currentVersion)) <= 0)
        {
            throw new InvalidDataException("The update is not newer than the installed version.");
        }

        ValidateArtifact(descriptor.TargetManifest, descriptor.Version, "manifest");
        ValidateArtifact(descriptor.FullPackage, descriptor.Version, "full");
        ValidateArtifact(descriptor.PortablePackage, descriptor.Version, "portable");
        if (descriptor.Bootstrapper is not null)
        {
            ValidateArtifact(descriptor.Bootstrapper, descriptor.Version, "bootstrapper");
        }

        var ids = new HashSet<string>(StringComparer.Ordinal);
        ids.Add(descriptor.TargetManifest.Id);
        ids.Add(descriptor.FullPackage.Id);
        ids.Add(descriptor.PortablePackage.Id);
        if (descriptor.Bootstrapper is not null && !ids.Add(descriptor.Bootstrapper.Id))
        {
            throw new InvalidDataException("The release contains duplicate artifact IDs.");
        }

        foreach (var delta in descriptor.Deltas)
        {
            ArgumentNullException.ThrowIfNull(delta);
            var baseVersion = UpdateVersion.Parse(delta.BaseVersion);
            if (baseVersion.CompareTo(version) >= 0
                || delta.Id != $"delta:{delta.BaseVersion}"
                || !ids.Add(delta.Id))
            {
                throw new InvalidDataException("The release contains an invalid delta artifact.");
            }
            ValidateArtifact(delta, descriptor.Version, delta.Id);
        }
    }

    public static void ValidateManifest(ReleaseManifest manifest, string expectedVersion)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        UpdateVersion.Parse(expectedVersion);
        if (manifest.SchemaVersion != UpdateProtocol.SchemaVersion
            || !string.Equals(manifest.Version, expectedVersion, StringComparison.Ordinal)
            || manifest.Files is null || manifest.Files.Count == 0)
        {
            throw new InvalidDataException("The target release manifest is invalid.");
        }

        var unique = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var hasDesktopExecutable = false;
        foreach (var file in manifest.Files)
        {
            ArgumentNullException.ThrowIfNull(file);
            var path = UpdatePaths.NormalizeRelativePath(file.Path);
            if (!string.Equals(path, file.Path, StringComparison.Ordinal)
                || !unique.Add(path)
                || file.Size < 0
                || !IsSha256(file.Sha256)
                || string.Equals(path, UpdateProtocol.InstalledManifestFileName,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException($"Invalid target manifest entry: {file.Path}");
            }

            hasDesktopExecutable |= string.Equals(path,
                UpdateProtocol.DesktopExecutableName, StringComparison.OrdinalIgnoreCase);
        }

        if (!hasDesktopExecutable)
        {
            throw new InvalidDataException(
                $"The target manifest does not contain {UpdateProtocol.DesktopExecutableName}.");
        }
    }

    public static void ValidateBootstrapperState(BootstrapperState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (state.SchemaVersion != UpdateProtocol.SchemaVersion
            || !UpdateVersion.TryParse(state.CurrentVersion, out _)
            || state.RollbackVersion is not null
            && !UpdateVersion.TryParse(state.RollbackVersion, out _)
            || state.PendingHealthToken is not null
            && !Guid.TryParse(state.PendingHealthToken, out _)
            || !UpdateVersion.TryParse(state.BootstrapperVersion, out _))
        {
            throw new InvalidDataException("Prometheus current.json is invalid.");
        }
    }

    public static void ValidateArtifact(UpdateArtifact artifact, string version,
        string expectedId)
    {
        ArgumentNullException.ThrowIfNull(artifact);
        ValidateArtifactCore(artifact.Id, artifact.ObjectKey, artifact.Size, artifact.Sha256,
            version, expectedId);
    }

    public static void ValidateArtifact(DeltaArtifact artifact, string version,
        string expectedId)
    {
        ArgumentNullException.ThrowIfNull(artifact);
        ValidateArtifactCore(artifact.Id, artifact.ObjectKey, artifact.Size, artifact.Sha256,
            version, expectedId);
    }

    private static void ValidateArtifactCore(string id, string objectKey, long size,
        string sha256, string version, string expectedId)
    {
        var prefix = $"releases/{version}/{UpdateProtocol.WindowsX64Rid}/";
        string normalizedObjectKey;
        try
        {
            normalizedObjectKey = UpdatePaths.NormalizeRelativePath(objectKey);
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidDataException)
        {
            throw new InvalidDataException("The release contains an invalid artifact key.",
                exception);
        }

        if (!string.Equals(id, expectedId, StringComparison.Ordinal)
            || size <= 0
            || !IsSha256(sha256)
            || !string.Equals(normalizedObjectKey, objectKey, StringComparison.Ordinal)
            || !objectKey.StartsWith(prefix, StringComparison.Ordinal))
        {
            throw new InvalidDataException("The release contains an invalid artifact.");
        }
    }

    public static bool ArtifactsMatch(UpdateArtifact left, UpdateArtifact right)
    {
        return string.Equals(left.Id, right.Id, StringComparison.Ordinal)
               && string.Equals(left.ObjectKey, right.ObjectKey, StringComparison.Ordinal)
               && left.Size == right.Size
               && string.Equals(left.Sha256, right.Sha256, StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsSha256(string? value)
    {
        return value is { Length: 64 }
               && value.All(character => character is >= '0' and <= '9'
                   or >= 'a' and <= 'f' or >= 'A' and <= 'F');
    }
}

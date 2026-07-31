using System.IO.Compression;
using System.Text.Json;
using Prometheus.Update;

namespace Prometheus.Updater;

internal static class UpdateInstaller
{
    public static Task VerifyArtifactAsync(string path, UpdateArtifact artifact)
    {
        return UpdateSecurity.VerifyFileAsync(path, artifact.Size, artifact.Sha256);
    }

    public static async Task<string> BuildTargetVersionAsync(UpdateApplyPlan plan,
        ReleaseManifest targetManifest,
        Func<SignedEnvelope, ReleaseManifest>? verifyManifest = null)
    {
        ValidateTargetManifest(plan, targetManifest);
        var installRoot = Path.GetFullPath(plan.InstallRoot);
        var stagingRoot = Path.Combine(installRoot, ".staging", plan.TargetVersion);
        var versionsRoot = Path.Combine(installRoot, "versions");
        var targetRoot = Path.Combine(versionsRoot, plan.TargetVersion);
        RecreateDirectory(stagingRoot);

        if (plan.PackageKind == UpdatePackageKind.Delta)
        {
            await BuildDeltaStagingAsync(plan, targetManifest, stagingRoot, verifyManifest)
                .ConfigureAwait(false);
        }
        else
        {
            await ExtractFullPackageAsync(plan.PackagePath, targetManifest, stagingRoot)
                .ConfigureAwait(false);
        }

        await VerifyDirectoryAsync(stagingRoot, targetManifest).ConfigureAwait(false);
        File.Copy(plan.TargetManifestPath,
            Path.Combine(stagingRoot, UpdateProtocol.InstalledManifestFileName), true);

        Directory.CreateDirectory(versionsRoot);
        if (Directory.Exists(targetRoot))
        {
            Directory.Delete(targetRoot, true);
        }
        Directory.Move(stagingRoot, targetRoot);
        return targetRoot;
    }

    public static void ValidateTargetManifest(UpdateApplyPlan plan, ReleaseManifest manifest)
    {
        UpdateValidation.ValidateManifest(manifest, plan.TargetVersion);
    }

    public static UpdateArtifact ResolveSelectedArtifact(ReleaseDescriptor descriptor, string id)
    {
        if (string.Equals(descriptor.FullPackage.Id, id, StringComparison.Ordinal))
        {
            return descriptor.FullPackage;
        }

        var delta = descriptor.Deltas.FirstOrDefault(value =>
            string.Equals(value.Id, id, StringComparison.Ordinal));
        if (delta is null)
        {
            throw new InvalidDataException(
                "The selected package is not authorized by the release descriptor.");
        }

        return new UpdateArtifact
        {
            Id = delta.Id,
            ObjectKey = delta.ObjectKey,
            Size = delta.Size,
            Sha256 = delta.Sha256
        };
    }

    public static void CleanupOldVersions(string versionsRoot, string current, string rollback)
    {
        foreach (var directory in Directory.EnumerateDirectories(versionsRoot))
        {
            var name = Path.GetFileName(directory);
            if (!string.Equals(name, current, StringComparison.OrdinalIgnoreCase)
                && !string.Equals(name, rollback, StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    Directory.Delete(directory, true);
                }
                catch (Exception exception)
                {
                    BootstrapperLog.Write(
                        $"Unable to remove old version {name}: {exception.Message}");
                }
            }
        }
    }

    private static async Task BuildDeltaStagingAsync(UpdateApplyPlan plan,
        ReleaseManifest targetManifest, string stagingRoot,
        Func<SignedEnvelope, ReleaseManifest>? verifyManifest)
    {
        var baseRoot = Path.Combine(plan.InstallRoot, "versions", plan.CurrentVersion);
        var baseManifestPath = Path.Combine(baseRoot, UpdateProtocol.InstalledManifestFileName);
        var baseEnvelope = JsonSerializer.Deserialize(await File.ReadAllBytesAsync(baseManifestPath),
            UpdateJsonContext.Default.SignedEnvelope)
            ?? throw new InvalidDataException("The installed base manifest is empty.");
        var baseManifest = verifyManifest is null
            ? UpdateSecurity.VerifyAndDeserialize(baseEnvelope,
                UpdateJsonContext.Default.ReleaseManifest)
            : verifyManifest(baseEnvelope);
        UpdateValidation.ValidateManifest(baseManifest, plan.CurrentVersion);

        var baseFiles = baseManifest.Files.ToDictionary(
            file => UpdatePaths.NormalizeRelativePath(file.Path), StringComparer.OrdinalIgnoreCase);
        using var archive = ZipFile.OpenRead(plan.PackagePath);
        var entries = GetSafeEntries(archive);
        var usedEntries = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var targetFile in targetManifest.Files)
        {
            var relative = UpdatePaths.NormalizeRelativePath(targetFile.Path);
            var destination = UpdatePaths.ResolveUnderRoot(stagingRoot, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            if (entries.TryGetValue(relative, out var entry))
            {
                await ExtractEntryAsync(entry, destination).ConfigureAwait(false);
                usedEntries.Add(relative);
                continue;
            }

            if (!baseFiles.TryGetValue(relative, out var baseFile)
                || !string.Equals(baseFile.Sha256, targetFile.Sha256,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException($"Delta package is missing {relative}.");
            }

            var source = UpdatePaths.ResolveUnderRoot(baseRoot, relative);
            await UpdateSecurity.VerifyFileAsync(source, baseFile.Size, baseFile.Sha256)
                .ConfigureAwait(false);
            if (!NativeMethods.CreateHardLink(destination, source, IntPtr.Zero))
            {
                File.Copy(source, destination, true);
            }
        }

        if (entries.Keys.Any(path => !usedEntries.Contains(path)))
        {
            throw new InvalidDataException(
                "The delta package contains files not present in the target manifest.");
        }
    }

    private static async Task ExtractFullPackageAsync(string packagePath,
        ReleaseManifest manifest, string stagingRoot)
    {
        var expected = manifest.Files.Select(file => UpdatePaths.NormalizeRelativePath(file.Path))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        using var archive = ZipFile.OpenRead(packagePath);
        var entries = GetSafeEntries(archive);
        if (!expected.SetEquals(entries.Keys))
        {
            throw new InvalidDataException(
                "The full package contents do not match the target manifest.");
        }

        foreach (var (relative, entry) in entries)
        {
            var destination = UpdatePaths.ResolveUnderRoot(stagingRoot, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            await ExtractEntryAsync(entry, destination).ConfigureAwait(false);
        }
    }

    private static Dictionary<string, ZipArchiveEntry> GetSafeEntries(ZipArchive archive)
    {
        var entries = new Dictionary<string, ZipArchiveEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in archive.Entries)
        {
            if (string.IsNullOrEmpty(entry.Name))
            {
                continue;
            }

            var unixMode = (entry.ExternalAttributes >> 16) & 0xF000;
            if (unixMode == 0xA000)
            {
                throw new InvalidDataException(
                    "Symbolic links are not allowed in update packages.");
            }

            var relative = UpdatePaths.NormalizeRelativePath(entry.FullName);
            if (!entries.TryAdd(relative, entry))
            {
                throw new InvalidDataException($"Duplicate update entry: {relative}");
            }
        }

        return entries;
    }

    private static async Task ExtractEntryAsync(ZipArchiveEntry entry, string destination)
    {
        await using var source = entry.Open();
        await using var target = new FileStream(destination, FileMode.CreateNew, FileAccess.Write,
            FileShare.None, 128 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
        await source.CopyToAsync(target).ConfigureAwait(false);
    }

    private static async Task VerifyDirectoryAsync(string root, ReleaseManifest manifest)
    {
        foreach (var file in manifest.Files)
        {
            var path = UpdatePaths.ResolveUnderRoot(root, file.Path);
            await UpdateSecurity.VerifyFileAsync(path, file.Size, file.Sha256)
                .ConfigureAwait(false);
        }
    }

    private static void RecreateDirectory(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, true);
        }
        Directory.CreateDirectory(path);
    }
}

using System.Diagnostics;
using System.IO.Compression;
using Prometheus.Update;

namespace Prometheus.Updater;

internal static class UpdateInstaller
{
    public static async Task<string> PrepareStagingAsync(UpdateApplyPlan plan,
        Func<string, string, bool>? validateDesktopVersion = null)
    {
        UpdateValidation.ValidateApplyPlan(plan);
        await UpdateSecurity.VerifyFileAsync(plan.PackagePath, plan.PackageSize,
            plan.PackageSha256).ConfigureAwait(false);

        var installRoot = NormalizeInstallRoot(plan.InstallRoot);
        var stagingRoot = installRoot + ".update-staging";
        RecreateDirectory(stagingRoot);
        try
        {
            using var archive = ZipFile.OpenRead(plan.PackagePath);
            var entries = GetSafeEntries(archive);
            foreach (var (relativePath, entry) in entries)
            {
                var destination = UpdatePaths.ResolveUnderRoot(stagingRoot, relativePath);
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                await ExtractEntryAsync(entry, destination).ConfigureAwait(false);
            }

            var desktopPath = Path.Combine(stagingRoot,
                UpdateProtocol.DesktopExecutableName);
            if (!File.Exists(desktopPath))
            {
                throw new InvalidDataException(
                    $"The update package does not contain {UpdateProtocol.DesktopExecutableName} at its root.");
            }

            var isExpectedVersion = validateDesktopVersion is null
                ? HasExpectedDesktopVersion(desktopPath, plan.TargetVersion)
                : validateDesktopVersion(desktopPath, plan.TargetVersion);
            if (!isExpectedVersion)
            {
                throw new InvalidDataException(
                    "The desktop executable version does not match the GitHub Release tag.");
            }
            return stagingRoot;
        }
        catch
        {
            TryDeleteDirectory(stagingRoot);
            throw;
        }
    }

    public static string GetBackupRoot(string installRoot)
    {
        return NormalizeInstallRoot(installRoot) + ".rollback";
    }

    public static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, true);
            }
        }
        catch (Exception exception)
        {
            BootstrapperLog.Write(
                $"Unable to remove update directory {path}: {exception.Message}");
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

            var unixFileType = (entry.ExternalAttributes >> 16) & 0xF000;
            var windowsAttributes = (FileAttributes)(entry.ExternalAttributes & 0xFFFF);
            if (unixFileType == 0xA000
                || windowsAttributes.HasFlag(FileAttributes.ReparsePoint))
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

    private static bool HasExpectedDesktopVersion(string desktopPath, string targetVersion)
    {
        var productVersion = FileVersionInfo.GetVersionInfo(desktopPath).ProductVersion?
            .Split('+')[0];
        return string.Equals(productVersion, targetVersion, StringComparison.Ordinal);
    }

    private static string NormalizeInstallRoot(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        var installRoot = Path.GetFullPath(value).TrimEnd(Path.DirectorySeparatorChar,
            Path.AltDirectorySeparatorChar);
        if (string.IsNullOrWhiteSpace(Path.GetFileName(installRoot))
            || Directory.GetParent(installRoot) is null)
        {
            throw new InvalidDataException("The Prometheus installation directory is invalid.");
        }
        return installRoot;
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

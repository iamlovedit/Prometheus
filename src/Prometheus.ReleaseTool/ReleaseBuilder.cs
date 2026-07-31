using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using Prometheus.Update;

namespace Prometheus.ReleaseTool;

internal sealed class ReleaseBuildResult
{
    public ReleaseDescriptor Descriptor { get; init; } = new();
    public SignedEnvelope ReleaseEnvelope { get; init; } = new();
    public SignedEnvelope ManifestEnvelope { get; init; } = new();
    public List<UploadMapEntry> Uploads { get; init; } = [];
}

internal static class ReleaseBuilder
{
    private static readonly DateTimeOffset ZipTimestamp =
        new(2000, 1, 1, 0, 0, 0, TimeSpan.Zero);

    public static async Task<ReleaseBuildResult> BuildAsync(ReleaseOptions options,
        ECDsa signingKey, IReadOnlyList<SignedEnvelope> previousManifestEnvelopes,
        CancellationToken cancellationToken = default)
    {
        if (signingKey.KeySize != 256)
        {
            throw new CryptographicException("The update signing key must use ECDSA P-256.");
        }
        var version = UpdateVersion.Parse(options.Version).ToString();
        UpdateVersion.Parse(options.MinimumSupportedVersion);
        UpdateVersion.Parse(options.MinimumBootstrapperVersion);
        UpdateVersion.Parse(options.BootstrapperVersion);
        ValidateInputs(options, version);
        RecreateDirectory(options.OutputDirectory);

        var manifest = await CreateManifestAsync(options.PublishDirectory, version,
            cancellationToken).ConfigureAwait(false);
        var manifestEnvelope = UpdateSecurity.Sign(manifest, signingKey,
            UpdateJsonContext.Default.ReleaseManifest);
        var manifestPath = Path.Combine(options.OutputDirectory, "manifest.json");
        WriteEnvelope(manifestPath, manifestEnvelope);

        var fullPath = Path.Combine(options.OutputDirectory, "full.zip");
        CreateFilesZip(fullPath, options.PublishDirectory, manifest.Files);
        var prefix = $"releases/{version}/{UpdateProtocol.WindowsX64Rid}";
        var fullArtifact = await CreateArtifactAsync("full", $"{prefix}/full.zip", fullPath,
            cancellationToken).ConfigureAwait(false);
        var manifestArtifact = await CreateArtifactAsync("manifest",
            $"{prefix}/manifest.json", manifestPath, cancellationToken).ConfigureAwait(false);

        var bootstrapperCopy = Path.Combine(options.OutputDirectory,
            UpdateProtocol.BootstrapperExecutableName);
        File.Copy(options.BootstrapperPath, bootstrapperCopy, true);
        var bootstrapperArtifact = await CreateArtifactAsync("bootstrapper",
            $"{prefix}/{UpdateProtocol.BootstrapperExecutableName}", bootstrapperCopy,
            cancellationToken).ConfigureAwait(false);

        var portablePath = Path.Combine(options.OutputDirectory,
            $"Prometheus-{version}-{UpdateProtocol.WindowsX64Rid}.zip");
        CreatePortableZip(portablePath, options.PublishDirectory, manifest.Files,
            bootstrapperCopy, version, options.BootstrapperVersion, manifestEnvelope);
        var portableArtifact = await CreateArtifactAsync("portable",
            $"{prefix}/Prometheus-{version}-{UpdateProtocol.WindowsX64Rid}.zip", portablePath,
            cancellationToken).ConfigureAwait(false);

        var deltas = new List<DeltaArtifact>();
        var deltaPaths = new List<(DeltaArtifact Artifact, string Path)>();
        using var verificationKey = ECDsa.Create();
        verificationKey.ImportSubjectPublicKeyInfo(signingKey.ExportSubjectPublicKeyInfo(), out _);
        foreach (var previousEnvelope in previousManifestEnvelopes.Take(3))
        {
            var previous = UpdateSecurity.VerifyAndDeserialize(previousEnvelope,
                verificationKey, UpdateJsonContext.Default.ReleaseManifest);
            UpdateValidation.ValidateManifest(previous, previous.Version);
            if (UpdateVersion.Parse(previous.Version).CompareTo(UpdateVersion.Parse(version)) >= 0)
            {
                continue;
            }

            var changedFiles = GetChangedFiles(previous, manifest);
            var deltaPath = Path.Combine(options.OutputDirectory,
                $"delta-from-{previous.Version}.zip");
            CreateFilesZip(deltaPath, options.PublishDirectory, changedFiles);
            var deltaInfo = new FileInfo(deltaPath);
            if (deltaInfo.Length * 100 >= new FileInfo(fullPath).Length * 70)
            {
                File.Delete(deltaPath);
                continue;
            }

            var artifact = new DeltaArtifact
            {
                Id = $"delta:{previous.Version}",
                BaseVersion = previous.Version,
                ObjectKey = $"{prefix}/deltas/from-{previous.Version}.zip",
                Size = deltaInfo.Length,
                Sha256 = await UpdateSecurity.ComputeSha256Async(deltaPath, cancellationToken)
                    .ConfigureAwait(false)
            };
            deltas.Add(artifact);
            deltaPaths.Add((artifact, deltaPath));
        }

        var descriptor = new ReleaseDescriptor
        {
            Version = version,
            MinimumSupportedVersion = options.MinimumSupportedVersion,
            MinimumBootstrapperVersion = options.MinimumBootstrapperVersion,
            BootstrapperVersion = options.BootstrapperVersion,
            PublishedAt = DateTimeOffset.UtcNow,
            RolloutPercentage = 100,
            TargetManifest = manifestArtifact,
            FullPackage = fullArtifact,
            PortablePackage = portableArtifact,
            Bootstrapper = bootstrapperArtifact,
            Deltas = deltas,
            ReleaseNotes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["zh-CN"] = options.NotesZh,
                ["en-US"] = options.NotesEn
            }
        };
        UpdateValidation.ValidateReleaseDescriptor(descriptor);
        var releaseEnvelope = UpdateSecurity.Sign(descriptor, signingKey,
            UpdateJsonContext.Default.ReleaseDescriptor);
        var releasePath = Path.Combine(options.OutputDirectory, "release.json");
        WriteEnvelope(releasePath, releaseEnvelope);

        var uploads = new List<UploadMapEntry>
        {
            Map(releasePath, $"{prefix}/release.json", "application/json"),
            Map(manifestPath, manifestArtifact.ObjectKey, "application/json"),
            Map(fullPath, fullArtifact.ObjectKey, "application/zip"),
            Map(portablePath, portableArtifact.ObjectKey, "application/zip"),
            Map(bootstrapperCopy, bootstrapperArtifact.ObjectKey,
                "application/vnd.microsoft.portable-executable")
        };
        uploads.AddRange(deltaPaths.Select(delta =>
            Map(delta.Path, delta.Artifact.ObjectKey, "application/zip")));

        return new ReleaseBuildResult
        {
            Descriptor = descriptor,
            ReleaseEnvelope = releaseEnvelope,
            ManifestEnvelope = manifestEnvelope,
            Uploads = uploads
        };
    }

    private static async Task<ReleaseManifest> CreateManifestAsync(string publishDirectory,
        string version, CancellationToken cancellationToken)
    {
        var files = new List<ReleaseFileEntry>();
        foreach (var path in Directory.EnumerateFiles(publishDirectory, "*",
                     SearchOption.AllDirectories).OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
        {
            if (path.EndsWith(".pdb", StringComparison.OrdinalIgnoreCase)
                || string.Equals(Path.GetFileName(path), UpdateProtocol.InstalledManifestFileName,
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var relative = UpdatePaths.NormalizeRelativePath(
                Path.GetRelativePath(publishDirectory, path));
            var info = new FileInfo(path);
            files.Add(new ReleaseFileEntry
            {
                Path = relative,
                Size = info.Length,
                Sha256 = await UpdateSecurity.ComputeSha256Async(path, cancellationToken)
                    .ConfigureAwait(false)
            });
        }

        if (files.Count == 0 || files.All(file =>
                !string.Equals(file.Path, UpdateProtocol.DesktopExecutableName,
                    StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidDataException(
                $"The publish directory does not contain {UpdateProtocol.DesktopExecutableName}.");
        }

        var manifest = new ReleaseManifest
        {
            Version = version,
            Files = files
        };
        UpdateValidation.ValidateManifest(manifest, version);
        return manifest;
    }

    private static IReadOnlyList<ReleaseFileEntry> GetChangedFiles(ReleaseManifest previous,
        ReleaseManifest current)
    {
        var previousFiles = previous.Files.ToDictionary(
            file => UpdatePaths.NormalizeRelativePath(file.Path), StringComparer.OrdinalIgnoreCase);
        return current.Files.Where(file =>
        {
            var path = UpdatePaths.NormalizeRelativePath(file.Path);
            return !previousFiles.TryGetValue(path, out var old)
                   || !string.Equals(old.Sha256, file.Sha256, StringComparison.OrdinalIgnoreCase);
        }).ToArray();
    }

    private static void CreateFilesZip(string outputPath, string root,
        IEnumerable<ReleaseFileEntry> files)
    {
        using var stream = new FileStream(outputPath, FileMode.Create, FileAccess.ReadWrite,
            FileShare.None);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Create);
        foreach (var file in files.OrderBy(file => file.Path, StringComparer.Ordinal))
        {
            AddFile(archive, UpdatePaths.ResolveUnderRoot(root, file.Path), file.Path);
        }
    }

    private static void CreatePortableZip(string outputPath, string publishDirectory,
        IEnumerable<ReleaseFileEntry> files, string bootstrapperPath, string version,
        string bootstrapperVersion, SignedEnvelope manifestEnvelope)
    {
        using var stream = new FileStream(outputPath, FileMode.Create, FileAccess.ReadWrite,
            FileShare.None);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Create);
        AddFile(archive, bootstrapperPath, UpdateProtocol.BootstrapperExecutableName);
        foreach (var file in files.OrderBy(file => file.Path, StringComparer.Ordinal))
        {
            AddFile(archive, UpdatePaths.ResolveUnderRoot(publishDirectory, file.Path),
                $"versions/{version}/{file.Path}");
        }
        AddBytes(archive, $"versions/{version}/{UpdateProtocol.InstalledManifestFileName}",
            JsonSerializer.SerializeToUtf8Bytes(manifestEnvelope,
                UpdateJsonContext.Default.SignedEnvelope));
        AddBytes(archive, "current.json", JsonSerializer.SerializeToUtf8Bytes(
            new BootstrapperState
            {
                CurrentVersion = version,
                BootstrapperVersion = bootstrapperVersion
            }, UpdateJsonContext.Default.BootstrapperState));
    }

    private static void AddFile(ZipArchive archive, string sourcePath, string entryPath)
    {
        var entry = archive.CreateEntry(entryPath.Replace('\\', '/'), CompressionLevel.Optimal);
        entry.LastWriteTime = ZipTimestamp;
        using var source = File.OpenRead(sourcePath);
        using var target = entry.Open();
        source.CopyTo(target);
    }

    private static void AddBytes(ZipArchive archive, string entryPath, ReadOnlySpan<byte> bytes)
    {
        var entry = archive.CreateEntry(entryPath.Replace('\\', '/'), CompressionLevel.Optimal);
        entry.LastWriteTime = ZipTimestamp;
        using var target = entry.Open();
        target.Write(bytes);
    }

    private static async Task<UpdateArtifact> CreateArtifactAsync(string id, string objectKey,
        string path, CancellationToken cancellationToken)
    {
        var info = new FileInfo(path);
        return new UpdateArtifact
        {
            Id = id,
            ObjectKey = objectKey,
            Size = info.Length,
            Sha256 = await UpdateSecurity.ComputeSha256Async(path, cancellationToken)
                .ConfigureAwait(false)
        };
    }

    private static UploadMapEntry Map(string localPath, string objectKey, string contentType)
    {
        return new UploadMapEntry
        {
            LocalPath = localPath,
            ObjectKey = objectKey,
            ContentType = contentType
        };
    }

    private static void WriteEnvelope(string path, SignedEnvelope envelope)
    {
        File.WriteAllBytes(path, JsonSerializer.SerializeToUtf8Bytes(envelope,
            UpdateJsonContext.Default.SignedEnvelope));
    }

    private static void ValidateInputs(ReleaseOptions options, string version)
    {
        if (!string.Equals(options.GitTag, $"v{version}", StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Git tag {options.GitTag} does not match release {version}.");
        }
        if (!Directory.Exists(options.PublishDirectory))
        {
            throw new DirectoryNotFoundException(options.PublishDirectory);
        }
        if (!File.Exists(options.BootstrapperPath))
        {
            throw new FileNotFoundException("The Native AOT bootstrapper was not found.",
                options.BootstrapperPath);
        }

        var propsPath = Path.Combine(options.RepositoryRoot, "Directory.Build.props");
        var propsText = File.ReadAllText(propsPath);
        if (!propsText.Contains($"<Version>{version}</Version>", StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Directory.Build.props version does not match release {version}.");
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

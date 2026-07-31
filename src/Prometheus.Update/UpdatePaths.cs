using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using System.Globalization;

namespace Prometheus.Update;

public static class UpdatePaths
{
    public static string NormalizeRelativePath(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        if (value.IndexOf('\0') >= 0 || Path.IsPathRooted(value) || value.Contains(':'))
        {
            throw new InvalidDataException($"Unsafe update path: {value}");
        }

        var segments = value.Replace('\\', '/').Split('/');
        if (segments.Length == 0 || segments.Any(IsUnsafeSegment))
        {
            throw new InvalidDataException($"Unsafe update path: {value}");
        }

        return string.Join('/', segments);
    }

    public static string ResolveUnderRoot(string root, string relativePath)
    {
        var normalized = NormalizeRelativePath(relativePath);
        var fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar,
            Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var candidate = Path.GetFullPath(Path.Combine(fullRoot,
            normalized.Replace('/', Path.DirectorySeparatorChar)));
        if (!candidate.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException($"Update path escapes its root: {relativePath}");
        }

        return candidate;
    }

    public static string GetLocalDataRoot()
    {
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Prometheus");
    }

    public static string GetHealthMarkerPath(string token)
    {
        if (!Guid.TryParse(token, out _))
        {
            throw new InvalidDataException("The update health token is invalid.");
        }

        return Path.Combine(GetLocalDataRoot(), "Updates", "health", token + ".ready");
    }

    public static void WriteJsonAtomic<T>(string path, T value, JsonTypeInfo<T> typeInfo)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var temporaryPath = path + $".{Guid.NewGuid():N}.tmp";
        try
        {
            var bytes = JsonSerializer.SerializeToUtf8Bytes(value, typeInfo);
            using (var stream = new FileStream(temporaryPath, FileMode.CreateNew,
                       FileAccess.Write, FileShare.None, 16 * 1024, FileOptions.WriteThrough))
            {
                stream.Write(bytes);
                stream.Flush(true);
            }
            File.Move(temporaryPath, path, true);
        }
        finally
        {
            File.Delete(temporaryPath);
        }
    }

    private static bool IsUnsafeSegment(string segment)
    {
        if (segment.Length == 0 || segment is "." or ".."
            || segment.EndsWith(' ') || segment.EndsWith('.'))
        {
            return true;
        }

        foreach (var character in segment)
        {
            if (character < 32 || character is '<' or '>' or '"' or '|' or '?' or '*')
            {
                return true;
            }
        }

        var stem = segment.Split('.')[0];
        return stem.Equals("CON", StringComparison.OrdinalIgnoreCase)
               || stem.Equals("PRN", StringComparison.OrdinalIgnoreCase)
               || stem.Equals("AUX", StringComparison.OrdinalIgnoreCase)
               || stem.Equals("NUL", StringComparison.OrdinalIgnoreCase)
               || IsNumberedDevice(stem, "COM")
               || IsNumberedDevice(stem, "LPT");
    }

    private static bool IsNumberedDevice(string value, string prefix)
    {
        return value.Length == 4
               && value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
               && value[3] is >= '1' and <= '9';
    }
}

public readonly record struct UpdateVersion(int Major, int Minor, int Patch) : IComparable<UpdateVersion>
{
    public static UpdateVersion Parse(string value)
    {
        if (!TryParse(value, out var version))
        {
            throw new FormatException($"Invalid update version: {value}");
        }

        return version;
    }

    public static bool TryParse(string? value, out UpdateVersion version)
    {
        version = default;
        var parts = value?.Split('.');
        if (parts is not { Length: 3 }
            || !TryParsePart(parts[0], out var major)
            || !TryParsePart(parts[1], out var minor)
            || !TryParsePart(parts[2], out var patch))
        {
            return false;
        }

        version = new UpdateVersion(major, minor, patch);
        return true;
    }

    private static bool TryParsePart(string value, out int result)
    {
        result = 0;
        if (value.Length == 0 || value.Length > 1 && value[0] == '0'
            || value.Any(character => character is < '0' or > '9'))
        {
            return false;
        }

        return int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out result);
    }

    public int CompareTo(UpdateVersion other)
    {
        var major = Major.CompareTo(other.Major);
        if (major != 0) return major;
        var minor = Minor.CompareTo(other.Minor);
        return minor != 0 ? minor : Patch.CompareTo(other.Patch);
    }

    public override string ToString() => $"{Major}.{Minor}.{Patch}";
}

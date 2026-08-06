using System.Security.Cryptography;

namespace Prometheus.Update;

public static class UpdateSecurity
{
    public static async Task<string> ComputeSha256Async(string path,
        CancellationToken cancellationToken = default)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
            128 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false);
        return Convert.ToHexStringLower(hash);
    }

    public static async Task VerifyFileAsync(string path, long expectedSize, string expectedSha256,
        CancellationToken cancellationToken = default)
    {
        if (!UpdateValidation.IsSha256(expectedSha256))
        {
            throw new InvalidDataException("The expected update hash is invalid.");
        }

        var info = new FileInfo(path);
        if (!info.Exists || info.Length != expectedSize)
        {
            throw new InvalidDataException($"Update file size mismatch: {path}");
        }

        var actual = await ComputeSha256Async(path, cancellationToken).ConfigureAwait(false);
        if (!string.Equals(actual, expectedSha256, StringComparison.OrdinalIgnoreCase))
        {
            throw new CryptographicException($"Update file hash mismatch: {path}");
        }
    }
}

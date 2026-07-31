using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace Prometheus.Update;

public static class UpdateSecurity
{
    public static SignedEnvelope Sign<T>(T value, ECDsa privateKey, JsonTypeInfo<T> typeInfo)
    {
        var payloadBytes = JsonSerializer.SerializeToUtf8Bytes(value, typeInfo);
        var signature = privateKey.SignData(payloadBytes, HashAlgorithmName.SHA256,
            DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
        return new SignedEnvelope
        {
            Payload = Base64UrlEncode(payloadBytes),
            Signature = Base64UrlEncode(signature)
        };
    }

    public static T VerifyAndDeserialize<T>(SignedEnvelope envelope, JsonTypeInfo<T> typeInfo)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        var payload = Base64UrlDecode(envelope.Payload);
        var signature = Base64UrlDecode(envelope.Signature);
        using var publicKey = ECDsa.Create();
        publicKey.ImportSubjectPublicKeyInfo(Convert.FromBase64String(GeneratedUpdateTrust.PublicKeyBase64), out _);
        EnsureP256(publicKey);
        if (!publicKey.VerifyData(payload, signature, HashAlgorithmName.SHA256,
                DSASignatureFormat.IeeeP1363FixedFieldConcatenation))
        {
            throw new CryptographicException("The update signature is invalid.");
        }

        return JsonSerializer.Deserialize(payload, typeInfo)
            ?? throw new JsonException("The signed update payload is empty.");
    }

    public static T VerifyAndDeserialize<T>(SignedEnvelope envelope, ECDsa publicKey,
        JsonTypeInfo<T> typeInfo)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        ArgumentNullException.ThrowIfNull(publicKey);
        EnsureP256(publicKey);
        var payload = Base64UrlDecode(envelope.Payload);
        var signature = Base64UrlDecode(envelope.Signature);
        if (!publicKey.VerifyData(payload, signature, HashAlgorithmName.SHA256,
                DSASignatureFormat.IeeeP1363FixedFieldConcatenation))
        {
            throw new CryptographicException("The update signature is invalid.");
        }

        return JsonSerializer.Deserialize(payload, typeInfo)
            ?? throw new JsonException("The signed update payload is empty.");
    }

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

    public static string Base64UrlEncode(ReadOnlySpan<byte> value)
    {
        return Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    public static byte[] Base64UrlDecode(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        if (value.Length % 4 == 1 || value.Any(character =>
                character is not (>= 'A' and <= 'Z')
                and not (>= 'a' and <= 'z')
                and not (>= '0' and <= '9')
                and not '-' and not '_'))
        {
            throw new FormatException("The update envelope contains invalid Base64Url data.");
        }
        var normalized = value.Replace('-', '+').Replace('_', '/');
        normalized += (normalized.Length % 4) switch
        {
            2 => "==",
            3 => "=",
            _ => string.Empty
        };
        return Convert.FromBase64String(normalized);
    }

    private static void EnsureP256(ECDsa key)
    {
        if (key.KeySize != 256)
        {
            throw new CryptographicException("Update signatures must use ECDSA P-256.");
        }
    }
}

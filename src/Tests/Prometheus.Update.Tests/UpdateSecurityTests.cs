using System.Security.Cryptography;
using Prometheus.Update;

namespace Prometheus.Update.Tests;

public sealed class UpdateSecurityTests
{
    [Fact]
    public void VerifyAndDeserialize_WithValidEnvelope_ReturnsPayload()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var manifest = new ReleaseManifest
        {
            Version = "1.2.3",
            Files = [new ReleaseFileEntry { Path = "Prometheus.Desktop.exe", Size = 1,
                Sha256 = new string('a', 64) }]
        };
        var envelope = UpdateSecurity.Sign(manifest, key,
            UpdateJsonContext.Default.ReleaseManifest);
        using var publicKey = ECDsa.Create();
        publicKey.ImportSubjectPublicKeyInfo(key.ExportSubjectPublicKeyInfo(), out _);

        var result = UpdateSecurity.VerifyAndDeserialize(envelope, publicKey,
            UpdateJsonContext.Default.ReleaseManifest);

        Assert.Equal("1.2.3", result.Version);
    }

    [Fact]
    public void VerifyAndDeserialize_WhenPayloadIsChanged_Throws()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var envelope = UpdateSecurity.Sign(new ReleaseManifest { Version = "1.0.0" }, key,
            UpdateJsonContext.Default.ReleaseManifest);
        envelope.Payload = UpdateSecurity.Base64UrlEncode("tampered"u8);
        using var publicKey = ECDsa.Create();
        publicKey.ImportSubjectPublicKeyInfo(key.ExportSubjectPublicKeyInfo(), out _);

        Assert.Throws<CryptographicException>(() => UpdateSecurity.VerifyAndDeserialize(
            envelope, publicKey, UpdateJsonContext.Default.ReleaseManifest));
    }

    [Fact]
    public void VerifyAndDeserialize_WithNonP256Key_Throws()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP384);
        var envelope = UpdateSecurity.Sign(new ReleaseManifest { Version = "1.0.0" }, key,
            UpdateJsonContext.Default.ReleaseManifest);

        Assert.Throws<CryptographicException>(() => UpdateSecurity.VerifyAndDeserialize(
            envelope, key, UpdateJsonContext.Default.ReleaseManifest));
    }

    [Theory]
    [InlineData("abc=")]
    [InlineData("abc+")]
    [InlineData("a")]
    public void Base64UrlDecode_WhenInputIsNotCanonical_Throws(string value)
    {
        Assert.Throws<FormatException>(() => UpdateSecurity.Base64UrlDecode(value));
    }

    [Fact]
    public async Task VerifyFileAsync_WhenHashDoesNotMatch_Throws()
    {
        var path = Path.GetTempFileName();
        try
        {
            await File.WriteAllBytesAsync(path, [1, 2, 3]);
            await Assert.ThrowsAsync<CryptographicException>(() =>
                UpdateSecurity.VerifyFileAsync(path, 3, new string('0', 64)));
        }
        finally
        {
            File.Delete(path);
        }
    }
}

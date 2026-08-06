using System.Security.Cryptography;
using Prometheus.Update;

namespace Prometheus.Update.Tests;

public sealed class UpdateSecurityTests
{
    [Fact]
    public async Task VerifyFileAsync_WhenFileMatches_Completes()
    {
        var path = Path.GetTempFileName();
        try
        {
            await File.WriteAllBytesAsync(path, [1, 2, 3]);
            var hash = Convert.ToHexStringLower(SHA256.HashData([1, 2, 3]));

            await UpdateSecurity.VerifyFileAsync(path, 3, hash);
        }
        finally
        {
            File.Delete(path);
        }
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

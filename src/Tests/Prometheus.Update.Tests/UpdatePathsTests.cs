using Prometheus.Update;

namespace Prometheus.Update.Tests;

public sealed class UpdatePathsTests
{
    [Theory]
    [InlineData("../escape.dll")]
    [InlineData("a/../escape.dll")]
    [InlineData("C:/escape.dll")]
    [InlineData("/escape.dll")]
    [InlineData("a//escape.dll")]
    [InlineData("escape.dll/")]
    [InlineData("CON/file.dll")]
    [InlineData("file.dll.")]
    public void NormalizeRelativePath_WhenPathIsUnsafe_Throws(string value)
    {
        Assert.Throws<InvalidDataException>(() => UpdatePaths.NormalizeRelativePath(value));
    }

    [Fact]
    public void ResolveUnderRoot_WithNestedPath_StaysInsideRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));

        var result = UpdatePaths.ResolveUnderRoot(root, "modules/example.dll");

        Assert.StartsWith(Path.GetFullPath(root), result, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("1.0.0", "1.0.1", -1)]
    [InlineData("2.0.0", "1.9.9", 1)]
    [InlineData("1.2.3", "1.2.3", 0)]
    public void UpdateVersion_ComparisonIsNumeric(string left, string right, int sign)
    {
        Assert.Equal(sign, Math.Sign(UpdateVersion.Parse(left).CompareTo(UpdateVersion.Parse(right))));
    }

    [Theory]
    [InlineData("01.0.0")]
    [InlineData("+1.0.0")]
    [InlineData("1.0.0 ")]
    [InlineData("2147483648.0.0")]
    public void UpdateVersion_WhenNotCanonical_Rejects(string value)
    {
        Assert.False(UpdateVersion.TryParse(value, out _));
    }
}

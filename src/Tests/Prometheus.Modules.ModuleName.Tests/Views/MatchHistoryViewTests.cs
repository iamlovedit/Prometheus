using System.Runtime.CompilerServices;
using System.Xml.Linq;
using Xunit;

namespace Prometheus.Modules.ModuleName.Tests.Views
{
    public class MatchHistoryViewTests
    {
        [Fact]
        public void PreviewRunBindings_AreExplicitlyOneWay()
        {
            var document = XDocument.Load(GetViewPath());
            XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
            var bindings = document
                .Descendants(presentation + "Run")
                .Select(run => run.Attribute("Text")?.Value)
                .Where(value => value?.StartsWith("{Binding Preview.",
                    StringComparison.Ordinal) == true)
                .ToList();

            Assert.NotEmpty(bindings);
            Assert.All(bindings, binding =>
                Assert.Contains("Mode=OneWay", binding, StringComparison.Ordinal));
        }

        private static string GetViewPath(
            [CallerFilePath] string testFilePath = "")
        {
            return Path.GetFullPath(Path.Combine(
                Path.GetDirectoryName(testFilePath)!,
                "..",
                "..",
                "..",
                "Prometheus.Shared",
                "Views",
                "MatchHistoryView.xaml"));
        }
    }
}

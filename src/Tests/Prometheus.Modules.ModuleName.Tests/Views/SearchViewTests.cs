using System.Runtime.CompilerServices;
using System.Xml.Linq;
using Xunit;

namespace Prometheus.Modules.ModuleName.Tests.Views
{
    public class SearchViewTests
    {
        [Fact]
        public void SearchBar_IsHostedByPageHeaderWithoutSeparateSearchCard()
        {
            var document = XDocument.Load(GetViewPath());
            var borders = document.Descendants()
                .Where(element => element.Name.LocalName == "Border")
                .ToList();
            var header = Assert.Single(borders.Where(border =>
                border.Attribute("Style")?.Value.Contains(
                    "PageHeaderCard", StringComparison.Ordinal) == true));

            Assert.Contains(header.Descendants(), element =>
                element.Name.LocalName == "SearchBar");
            Assert.DoesNotContain(borders, border =>
                border.Attribute("Style")?.Value.Contains(
                    "PageCard", StringComparison.Ordinal) == true);
        }

        private static string GetViewPath(
            [CallerFilePath] string testFilePath = "")
        {
            return Path.GetFullPath(Path.Combine(
                Path.GetDirectoryName(testFilePath)!,
                "..",
                "..",
                "..",
                "Prometheus.Modules.Search",
                "Views",
                "SearchView.xaml"));
        }
    }
}

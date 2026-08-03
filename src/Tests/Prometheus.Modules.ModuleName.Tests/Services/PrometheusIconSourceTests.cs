using Prometheus.Desktop.Services;
using System.Windows.Media.Imaging;
using Xunit;

namespace Prometheus.Modules.ModuleName.Tests.Services
{
    public class PrometheusIconSourceTests
    {
        [Fact]
        public void Large_UsesLargestAvailableIconFrame()
        {
            var frame = Assert.IsAssignableFrom<BitmapFrame>(
                PrometheusIconSource.Large);

            Assert.Equal(256, frame.PixelWidth);
            Assert.Equal(256, frame.PixelHeight);
            Assert.True(frame.IsFrozen);
        }
    }
}

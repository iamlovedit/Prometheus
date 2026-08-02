using Prometheus.Desktop.Services;
using Xunit;

namespace Prometheus.Modules.ModuleName.Tests.Services
{
    public class LcuCompanionPlacementCalculatorTests
    {
        [Fact]
        public void Calculate_WhenRightSideHasSpace_AttachesToRightEdge()
        {
            var state = CreateState(
                new NativeWindowBounds(100, 100, 1100, 800),
                new NativeWindowBounds(0, 0, 1920, 1080));

            var placement = LcuCompanionPlacementCalculator.Calculate(state, 300);

            Assert.Equal(LcuCompanionSide.Right, placement.Side);
            Assert.Equal(1100, placement.Left);
            Assert.Equal(100, placement.Top);
            Assert.Equal(300, placement.Width);
            Assert.Equal(700, placement.Height);
        }

        [Fact]
        public void Calculate_WhenOnlyLeftSideHasSpace_AttachesToLeftEdge()
        {
            var state = CreateState(
                new NativeWindowBounds(500, 100, 1850, 800),
                new NativeWindowBounds(0, 0, 1920, 1080));

            var placement = LcuCompanionPlacementCalculator.Calculate(state, 300);

            Assert.Equal(LcuCompanionSide.Left, placement.Side);
            Assert.Equal(200, placement.Left);
        }

        [Fact]
        public void Calculate_WhenNeitherOutsideEdgeFits_UsesInsideRightEdge()
        {
            var state = CreateState(
                new NativeWindowBounds(100, 100, 900, 700),
                new NativeWindowBounds(0, 0, 1000, 800));

            var placement = LcuCompanionPlacementCalculator.Calculate(state, 300);

            Assert.Equal(LcuCompanionSide.InsideRight, placement.Side);
            Assert.Equal(600, placement.Left);
        }

        [Fact]
        public void Calculate_UsesLcuDpiForDesiredWidth()
        {
            var state = CreateState(
                new NativeWindowBounds(100, 100, 900, 700),
                new NativeWindowBounds(0, 0, 1600, 900),
                144);

            var placement = LcuCompanionPlacementCalculator.Calculate(state, 300);

            Assert.Equal(450, placement.Width);
        }

        private static LcuWindowState CreateState(
            NativeWindowBounds bounds,
            NativeWindowBounds workArea,
            int dpi = 96)
        {
            return new LcuWindowState(
                new IntPtr(1),
                bounds,
                workArea,
                dpi,
                true,
                false,
                true);
        }
    }
}

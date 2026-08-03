using Prometheus.Desktop.Services;
using Xunit;

namespace Prometheus.Modules.ModuleName.Tests.Services
{
    public class LcuCompanionChromeCalculatorTests
    {
        [Fact]
        public void Calculate_RightDock_UsesLeftSeamAndOuterRightCorners()
        {
            var chrome = LcuCompanionChromeCalculator.Calculate(
                LcuCompanionSide.Right);

            Assert.Equal(1, chrome.LeftBorderThickness);
            Assert.Equal(1, chrome.RightBorderThickness);
            Assert.Equal(0, chrome.TopLeftRadius);
            Assert.Equal(14, chrome.TopRightRadius);
            Assert.Equal(0, chrome.Inset);
            Assert.False(chrome.ShowShadow);
            Assert.Equal(LcuCompanionSeamSide.Left, chrome.SeamSide);
            Assert.Equal(3, chrome.SeamThickness);
        }

        [Fact]
        public void Calculate_LeftDock_MirrorsSeamAndCorners()
        {
            var chrome = LcuCompanionChromeCalculator.Calculate(
                LcuCompanionSide.Left);

            Assert.Equal(1, chrome.LeftBorderThickness);
            Assert.Equal(1, chrome.RightBorderThickness);
            Assert.Equal(14, chrome.TopLeftRadius);
            Assert.Equal(0, chrome.TopRightRadius);
            Assert.Equal(14, chrome.BottomLeftRadius);
            Assert.Equal(0, chrome.BottomRightRadius);
            Assert.False(chrome.ShowShadow);
            Assert.Equal(LcuCompanionSeamSide.Right, chrome.SeamSide);
        }

        [Fact]
        public void Calculate_InsideRight_UsesInsetFullChromeAndShadow()
        {
            var chrome = LcuCompanionChromeCalculator.Calculate(
                LcuCompanionSide.InsideRight);

            Assert.Equal(1, chrome.LeftBorderThickness);
            Assert.Equal(1, chrome.RightBorderThickness);
            Assert.Equal(14, chrome.TopLeftRadius);
            Assert.Equal(14, chrome.TopRightRadius);
            Assert.Equal(8, chrome.Inset);
            Assert.True(chrome.ShowShadow);
            Assert.Equal(LcuCompanionSeamSide.None, chrome.SeamSide);
            Assert.Equal(0, chrome.SeamThickness);
        }
    }
}

using Prometheus.Desktop.Services;
using Xunit;

namespace Prometheus.Modules.ModuleName.Tests.Services
{
    public class LcuCompanionZOrderCalculatorTests
    {
        [Fact]
        public void Calculate_WhenAnotherWindowPrecedesLcu_InsertsAfterThatWindow()
        {
            var lcuHandle = new IntPtr(10);
            var companionHandle = new IntPtr(20);
            var precedingHandle = new IntPtr(30);

            var result = LcuCompanionZOrderCalculator.Calculate(
                lcuHandle,
                companionHandle,
                window => window == lcuHandle ? precedingHandle : IntPtr.Zero);

            Assert.Equal(precedingHandle, result.InsertAfter);
            Assert.False(result.PreserveCurrent);
        }

        [Fact]
        public void Calculate_WhenCompanionAlreadyPrecedesLcu_PreservesZOrder()
        {
            var lcuHandle = new IntPtr(10);
            var companionHandle = new IntPtr(20);

            var result = LcuCompanionZOrderCalculator.Calculate(
                lcuHandle,
                companionHandle,
                _ => companionHandle);

            Assert.Equal(IntPtr.Zero, result.InsertAfter);
            Assert.True(result.PreserveCurrent);
        }

        [Fact]
        public void Calculate_WhenLcuIsTopWindow_InsertsCompanionAtTop()
        {
            var result = LcuCompanionZOrderCalculator.Calculate(
                new IntPtr(10),
                new IntPtr(20),
                _ => IntPtr.Zero);

            Assert.Equal(IntPtr.Zero, result.InsertAfter);
            Assert.False(result.PreserveCurrent);
        }
    }
}

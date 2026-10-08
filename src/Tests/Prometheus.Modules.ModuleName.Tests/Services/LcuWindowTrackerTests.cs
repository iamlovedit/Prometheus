using Prometheus.Desktop.Services;
using Xunit;

namespace Prometheus.Modules.ModuleName.Tests.Services
{
    public class LcuWindowTrackerTests
    {
        [Fact]
        public void WindowState_PreservesForegroundState()
        {
            var state = new LcuWindowState(
                new IntPtr(123),
                new NativeWindowBounds(0, 0, 800, 600),
                new NativeWindowBounds(0, 0, 1920, 1080),
                96,
                true,
                false,
                true);

            Assert.True(state.IsForeground);
        }

        [Fact]
        public void IsMainWindowCandidate_WhenWindowIsCloaked_ReturnsFalse()
        {
            var result = LcuWindowTracker.IsMainWindowCandidate(
                new IntPtr(123),
                expectedProcessId: 42,
                actualProcessId: 42,
                isVisible: true,
                isCloaked: true);

            Assert.False(result);
        }

        [Fact]
        public void IsTrackedWindowCandidate_WhenCachedWindowIsHidden_ReturnsFalse()
        {
            var result = LcuWindowTracker.IsTrackedWindowCandidate(
                new IntPtr(123),
                expectedProcessId: 42,
                isWindow: true,
                actualProcessId: 42,
                isVisible: false,
                isCloaked: false);

            Assert.False(result);
        }

        [Fact]
        public void IsTrackedWindowCandidate_WhenCachedWindowBelongsToCurrentVisibleProcess_ReturnsTrue()
        {
            var result = LcuWindowTracker.IsTrackedWindowCandidate(
                new IntPtr(123),
                expectedProcessId: 42,
                isWindow: true,
                actualProcessId: 42,
                isVisible: true,
                isCloaked: false);

            Assert.True(result);
        }

        [Fact]
        public void IsTrackedWindowCandidate_WhenCachedWindowBelongsToAnotherProcess_ReturnsFalse()
        {
            var result = LcuWindowTracker.IsTrackedWindowCandidate(
                new IntPtr(123),
                expectedProcessId: 42,
                isWindow: true,
                actualProcessId: 99,
                isVisible: true,
                isCloaked: false);

            Assert.False(result);
        }
    }
}

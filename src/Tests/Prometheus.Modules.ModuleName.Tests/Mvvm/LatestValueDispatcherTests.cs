using Prometheus.Core.Mvvm;
using Xunit;

namespace Prometheus.Modules.ModuleName.Tests.Mvvm
{
    public class LatestValueDispatcherTests
    {
        [Fact]
        public void Publish_BeforeScheduledActionRuns_AppliesOnlyLatestValue()
        {
            var scheduled = new List<Action>();
            var applied = new List<int>();
            var dispatcher = new LatestValueDispatcher<int>(
                action => scheduled.Add(action),
                value => applied.Add(value));

            dispatcher.Publish(1);
            dispatcher.Publish(2);
            dispatcher.Publish(3);

            Assert.Single(scheduled);
            Assert.Empty(applied);

            scheduled[0]();

            Assert.Equal([3], applied);
        }

        [Fact]
        public void Publish_AfterDrain_SchedulesNextValue()
        {
            var scheduled = new List<Action>();
            var applied = new List<int>();
            var dispatcher = new LatestValueDispatcher<int>(
                action => scheduled.Add(action),
                value => applied.Add(value));

            dispatcher.Publish(1);
            scheduled[0]();
            dispatcher.Publish(2);

            Assert.Equal(2, scheduled.Count);
            scheduled[1]();

            Assert.Equal([1, 2], applied);
        }
    }
}

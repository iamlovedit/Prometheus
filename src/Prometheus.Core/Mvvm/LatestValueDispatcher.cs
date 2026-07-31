namespace Prometheus.Core.Mvvm
{
    /// <summary>
    /// Coalesces a stream of values into one scheduled action and applies only
    /// the latest value when the scheduler gets a chance to run it.
    /// </summary>
    public sealed class LatestValueDispatcher<T>
    {
        private readonly object _sync = new();
        private readonly Action<Action> _schedule;
        private readonly Action<T> _apply;
        private T _latest;
        private bool _hasLatest;
        private bool _scheduled;

        public LatestValueDispatcher(Action<Action> schedule, Action<T> apply)
        {
            _schedule = schedule ?? throw new ArgumentNullException(nameof(schedule));
            _apply = apply ?? throw new ArgumentNullException(nameof(apply));
        }

        public void Publish(T value)
        {
            lock (_sync)
            {
                _latest = value;
                _hasLatest = true;
                if (_scheduled)
                {
                    return;
                }

                _scheduled = true;
            }

            try
            {
                _schedule(Drain);
            }
            catch
            {
                lock (_sync)
                {
                    _scheduled = false;
                }

                throw;
            }
        }

        private void Drain()
        {
            T value;
            lock (_sync)
            {
                if (!_hasLatest)
                {
                    _scheduled = false;
                    return;
                }

                value = _latest;
                _latest = default;
                _hasLatest = false;
                _scheduled = false;
            }

            _apply(value);
        }
    }
}

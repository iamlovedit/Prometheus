using Prometheus.Services.Interfaces;
using Serilog.Core;
using Serilog.Events;

namespace Prometheus.Services
{
    /// <summary>
    /// Runtime Serilog filter backed by a persisted user preference. Disabling logging also
    /// clears the current in-memory history after the filter has stopped all new events.
    /// </summary>
    public sealed class LoggingControlService : ILoggingControlService, ILogEventFilter
    {
        private readonly object _sync = new();
        private readonly ILogHistoryService _logHistory;
        private readonly Action<bool> _persistSetting;
        private int _isEnabled;

        public LoggingControlService(
            bool initialEnabled,
            ILogHistoryService logHistory,
            Action<bool> persistSetting)
        {
            _logHistory = logHistory ?? throw new ArgumentNullException(nameof(logHistory));
            _persistSetting = persistSetting
                ?? throw new ArgumentNullException(nameof(persistSetting));
            _isEnabled = initialEnabled ? 1 : 0;
        }

        public bool IsEnabled => Volatile.Read(ref _isEnabled) == 1;

        public event EventHandler EnabledChanged;

        bool ILogEventFilter.IsEnabled(LogEvent logEvent)
        {
            return IsEnabled;
        }

        internal void EmitIfEnabled(ILogEventSink sink, LogEvent logEvent)
        {
            lock (_sync)
            {
                if (IsEnabled)
                {
                    sink.Emit(logEvent);
                }
            }
        }

        public void SetEnabled(bool enabled)
        {
            lock (_sync)
            {
                if (IsEnabled == enabled)
                {
                    return;
                }

                if (enabled)
                {
                    _persistSetting(true);
                    Volatile.Write(ref _isEnabled, 1);
                }
                else
                {
                    Volatile.Write(ref _isEnabled, 0);
                    try
                    {
                        _persistSetting(false);
                    }
                    catch
                    {
                        Volatile.Write(ref _isEnabled, 1);
                        throw;
                    }

                    _logHistory.Clear();
                }
            }

            EnabledChanged?.Invoke(this, EventArgs.Empty);
        }
    }
}

using Serilog.Core;
using Serilog.Events;

namespace Prometheus.Services
{
    /// <summary>
    /// Performs the authoritative enable-state check immediately around an underlying sink emit.
    /// This ensures disabling waits for accepted in-flight writes before clearing memory and that
    /// events which passed an earlier Serilog filter cannot arrive after the switch is off.
    /// </summary>
    public sealed class LoggingControlledSink : ILogEventSink, IDisposable
    {
        private readonly LoggingControlService _control;
        private readonly ILogEventSink _sink;

        public LoggingControlledSink(
            LoggingControlService control,
            ILogEventSink sink)
        {
            _control = control ?? throw new ArgumentNullException(nameof(control));
            _sink = sink ?? throw new ArgumentNullException(nameof(sink));
        }

        public void Emit(LogEvent logEvent)
        {
            ArgumentNullException.ThrowIfNull(logEvent);
            _control.EmitIfEnabled(_sink, logEvent);
        }

        public void Dispose()
        {
            if (_sink is IDisposable disposable)
            {
                disposable.Dispose();
            }
        }
    }
}

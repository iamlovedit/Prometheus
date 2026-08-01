namespace Prometheus.Services.Interfaces
{
    /// <summary>
    /// Controls whether application log events are allowed to reach any configured sink.
    /// The setting takes effect immediately and is expected to persist across restarts.
    /// </summary>
    public interface ILoggingControlService
    {
        /// <summary>Gets whether logging is currently enabled.</summary>
        bool IsEnabled { get; }

        /// <summary>Raised after the effective logging state changes.</summary>
        event EventHandler EnabledChanged;

        /// <summary>Persists and applies the requested logging state.</summary>
        void SetEnabled(bool enabled);
    }
}

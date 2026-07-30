using System;

namespace Prometheus.Core.Models
{
    /// <summary>
    /// Ordered log severity levels. The numeric values are significant:
    /// higher value means higher severity, so range/minimum comparisons work directly.
    /// </summary>
    public enum LogLevel
    {
        Verbose = 0,
        Debug = 1,
        Information = 2,
        Warning = 3,
        Error = 4,
        Fatal = 5,
    }

    /// <summary>
    /// Immutable snapshot of a single log event, decoupled from Serilog's <c>LogEvent</c>
    /// so that the in-memory log buffer can be consumed by view models without a Serilog dependency.
    /// </summary>
    public sealed class LogEntry
    {
        public DateTimeOffset Timestamp { get; }

        public LogLevel Level { get; }

        public string Message { get; }

        public string Exception { get; }

        public LogEntry(DateTimeOffset timestamp, LogLevel level, string message, string exception)
        {
            Timestamp = timestamp;
            Level = level;
            Message = message ?? string.Empty;
            Exception = string.IsNullOrEmpty(exception) ? null : exception;
        }
    }
}

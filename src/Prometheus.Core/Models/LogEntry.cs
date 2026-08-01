
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
    /// Stable log kinds used by the query UI. Events without an explicit kind are kept as
    /// <see cref="Unclassified"/> so ordinary Serilog diagnostics are never hidden.
    /// </summary>
    public enum LogEntryKind
    {
        Unclassified = 0,
        Operation = 1,
        Diagnostic = 2,
    }

    /// <summary>A privacy-reviewed structured property retained by the in-memory sink.</summary>
    public sealed class LogEntryProperty
    {
        public string Name { get; }

        public string Value { get; }

        public bool IsTruncated { get; }

        public LogEntryProperty(string name, string value, bool isTruncated = false)
        {
            Name = name ?? string.Empty;
            Value = value ?? string.Empty;
            IsTruncated = isTruncated;
        }
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

        public LogEntryKind Kind { get; }

        public string EventName { get; }

        public string Category { get; }

        public string Origin { get; }

        public string Outcome { get; }

        public string Module { get; }

        public string EventId { get; }

        public string OperationId { get; }

        public string AppSessionId { get; }

        public IReadOnlyList<LogEntryProperty> Properties { get; }

        public bool IsMessageTruncated { get; }

        public bool IsExceptionTruncated { get; }

        public bool HasException => !string.IsNullOrWhiteSpace(Exception);

        public bool HasProperties => Properties.Count > 0;

        public bool HasEventName => !string.IsNullOrWhiteSpace(EventName);

        public bool HasOrigin => !string.IsNullOrWhiteSpace(Origin);

        public bool HasOutcome => !string.IsNullOrWhiteSpace(Outcome);

        public bool HasModule => !string.IsNullOrWhiteSpace(Module);

        public string DisplayCategory => string.IsNullOrWhiteSpace(Category)
            ? "Application"
            : Category;

        public string LevelCode => Level switch
        {
            LogLevel.Verbose => "VRB",
            LogLevel.Debug => "DBG",
            LogLevel.Information => "INF",
            LogLevel.Warning => "WRN",
            LogLevel.Error => "ERR",
            LogLevel.Fatal => "FTL",
            _ => "INF",
        };

        public string DurationMs => GetPropertyValue("DurationMs");

        public bool HasDuration => !string.IsNullOrWhiteSpace(DurationMs);

        public LogEntry(DateTimeOffset timestamp, LogLevel level, string message, string exception)
            : this(
                timestamp,
                level,
                message,
                exception,
                LogEntryKind.Unclassified,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                [],
                false,
                false)
        {
        }

        public LogEntry(
            DateTimeOffset timestamp,
            LogLevel level,
            string message,
            string exception,
            LogEntryKind kind,
            string eventName,
            string category,
            string origin,
            string outcome,
            string module,
            string eventId,
            string operationId,
            string appSessionId,
            IReadOnlyList<LogEntryProperty> properties,
            bool isMessageTruncated,
            bool isExceptionTruncated)
        {
            Timestamp = timestamp;
            Level = level;
            Message = message ?? string.Empty;
            Exception = string.IsNullOrEmpty(exception) ? null : exception;
            Kind = kind;
            EventName = string.IsNullOrWhiteSpace(eventName) ? null : eventName;
            Category = string.IsNullOrWhiteSpace(category) ? null : category;
            Origin = string.IsNullOrWhiteSpace(origin) ? null : origin;
            Outcome = string.IsNullOrWhiteSpace(outcome) ? null : outcome;
            Module = string.IsNullOrWhiteSpace(module) ? null : module;
            EventId = string.IsNullOrWhiteSpace(eventId) ? null : eventId;
            OperationId = string.IsNullOrWhiteSpace(operationId) ? null : operationId;
            AppSessionId = string.IsNullOrWhiteSpace(appSessionId) ? null : appSessionId;
            Properties = properties ?? [];
            IsMessageTruncated = isMessageTruncated;
            IsExceptionTruncated = isExceptionTruncated;
        }

        public string GetPropertyValue(string name)
        {
            return Properties.FirstOrDefault(property =>
                string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))?.Value;
        }
    }
}

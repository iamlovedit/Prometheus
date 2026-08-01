using Prometheus.Core.Models;
using Prometheus.Core.Logging;
using Prometheus.Services.Interfaces;
using Serilog.Core;
using Serilog.Events;
using System.Globalization;

namespace Prometheus.Services
{
    /// <summary>
    /// Singleton in-memory log buffer with a Serilog <see cref="ILogEventSink"/> attached to the
    /// root logger. Keeps the most recent <see cref="Capacity"/> entries in a thread-safe ring
    /// buffer and surfaces them through <see cref="ILogHistoryService"/>. The file sink runs in
    /// parallel, so disk logging behaviour is unchanged.
    /// </summary>
    public sealed class LogHistoryService : ILogHistoryService
    {
        internal const string SkipInMemoryPropertyName = "SkipInMemoryLog";

        private const int MaxMessageLength = 4096;
        private const int MaxExceptionLength = 16384;
        private const int MaxPropertyValueLength = 4096;

        private static readonly string[] RetainedPropertyNames =
        [
            "ClientSessionId",
            "TargetType",
            "TargetId",
            "ActionId",
            "ChampionId",
            "RunePageId",
            "OldValue",
            "NewValue",
            "OldCount",
            "NewCount",
            "OldLength",
            "NewLength",
            "GameflowPhase",
            "ConnectionState",
            "PhaseInstance",
            "DurationMs",
            "AttemptCount",
            "ErrorType",
            "ErrorCode",
            "HttpStatusCode",
            "HasPassword",
            "SkinId",
            "ProfileIconId",
            "QueueType",
            "Tier",
            "Division",
            "IsEmpty",
            "TextLength",
            "QueryLength",
            "ResultCount",
            "Found",
            "AssetType",
            "AssetId",
            "FileExtension",
            "PreviousCount",
            "ClearScope",
            "EventType",
            "Uri",
            "Data",
            "DataRedactedFieldCount",
            "DataSanitizationFailed",
            "DataSanitizationErrorType",
            "SourceContext",
        ];

        private static readonly HashSet<string> WebsocketOnlyPropertyNames = new(
            StringComparer.Ordinal)
        {
            "EventType",
            "Uri",
            "Data",
            "DataRedactedFieldCount",
            "DataSanitizationFailed",
            "DataSanitizationErrorType",
        };

        private readonly object _sync = new();
        private readonly LogEntry[] _buffer;
        private int _head;
        private int _count;
        private readonly LogHistorySink _sink;

        public LogHistoryService(int capacity)
        {
            if (capacity <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(capacity), capacity, "Capacity must be positive.");
            }

            _buffer = new LogEntry[capacity];
            _sink = new LogHistorySink(this);
        }

        /// <summary>The Serilog sink to attach to the root logger configuration.</summary>
        public ILogEventSink Sink => _sink;

        /// <inheritdoc />
        public int Capacity => _buffer.Length;

        /// <inheritdoc />
        public event EventHandler<LogEntryLoggedEventArgs> EntryLogged;

        /// <inheritdoc />
        public event EventHandler Cleared;

        /// <summary>Called by the sink on the logging thread.</summary>
        internal void Capture(LogEvent logEvent)
        {
            if (logEvent is null || ShouldSkipInMemory(logEvent))
            {
                return;
            }

            var entry = ToEntry(logEvent);
            lock (_sync)
            {
                _buffer[_head] = entry;
                _head = (_head + 1) % _buffer.Length;
                if (_count < _buffer.Length)
                {
                    _count++;
                }
            }

            // Raise outside the lock so a slow handler cannot starve writers.
            EntryLogged?.Invoke(this, new LogEntryLoggedEventArgs(entry));
        }

        /// <inheritdoc />
        public IReadOnlyList<LogEntry> GetSnapshot()
        {
            lock (_sync)
            {
                var result = new List<LogEntry>(_count);
                var start = (_count < _buffer.Length) ? 0 : _head;
                for (var i = 0; i < _count; i++)
                {
                    var index = (start + i) % _buffer.Length;
                    if (_buffer[index] is { } entry)
                    {
                        result.Add(entry);
                    }
                }

                return result;
            }
        }

        /// <inheritdoc />
        public void Clear()
        {
            int previousCount;
            lock (_sync)
            {
                previousCount = _count;
                Array.Clear(_buffer, 0, _buffer.Length);
                _head = 0;
                _count = 0;
            }

            OperationLog.Write(
                LogEventLevel.Information,
                "diagnostics.logs.clear",
                "Diagnostics",
                "Manual",
                "Succeeded",
                Guid.NewGuid(),
                "LogPanel",
                "The current session log panel was cleared.",
                new Dictionary<string, object>
                {
                    ["PreviousCount"] = previousCount,
                    ["ClearScope"] = "CurrentSessionMemory",
                    [SkipInMemoryPropertyName] = true,
                });

            Cleared?.Invoke(this, EventArgs.Empty);
        }

        private static LogEntry ToEntry(LogEvent logEvent)
        {
            var message = Truncate(logEvent.RenderMessage(), MaxMessageLength,
                out var messageTruncated);
            var exception = Truncate(logEvent.Exception?.ToString(), MaxExceptionLength,
                out var exceptionTruncated);

            return new LogEntry(
                logEvent.Timestamp,
                MapLevel(logEvent.Level),
                message,
                exception,
                ParseKind(GetScalarText(logEvent, "Kind")),
                GetScalarText(logEvent, "EventName"),
                GetScalarText(logEvent, "Category"),
                GetScalarText(logEvent, "Origin"),
                GetScalarText(logEvent, "Outcome"),
                GetScalarText(logEvent, "Module"),
                GetScalarText(logEvent, "EventId"),
                GetScalarText(logEvent, "OperationId"),
                GetScalarText(logEvent, "AppSessionId"),
                GetRetainedProperties(logEvent),
                messageTruncated,
                exceptionTruncated);
        }

        private static IReadOnlyList<LogEntryProperty> GetRetainedProperties(LogEvent logEvent)
        {
            var properties = new List<LogEntryProperty>();
            var isWebsocketEvent = string.Equals(
                GetScalarText(logEvent, "EventName"),
                "lcu.websocket.event.received",
                StringComparison.Ordinal);
            foreach (var propertyName in RetainedPropertyNames)
            {
                if (WebsocketOnlyPropertyNames.Contains(propertyName) && !isWebsocketEvent)
                {
                    continue;
                }

                if (!logEvent.Properties.TryGetValue(propertyName, out var propertyValue))
                {
                    continue;
                }

                var value = ConvertPropertyValue(propertyValue);
                value = Truncate(value, MaxPropertyValueLength, out var truncated);
                properties.Add(new LogEntryProperty(propertyName, value, truncated));
            }

            return properties;
        }

        private static bool ShouldSkipInMemory(LogEvent logEvent)
        {
            return logEvent.Properties.TryGetValue(SkipInMemoryPropertyName, out var value)
                && value is ScalarValue { Value: true };
        }

        private static string GetScalarText(LogEvent logEvent, string propertyName)
        {
            return logEvent.Properties.TryGetValue(propertyName, out var value)
                ? ConvertPropertyValue(value)
                : null;
        }

        private static string ConvertPropertyValue(LogEventPropertyValue value)
        {
            if (value is not ScalarValue scalarValue)
            {
                return value?.ToString() ?? string.Empty;
            }

            return scalarValue.Value switch
            {
                null => string.Empty,
                DateTime dateTime => dateTime.ToString("O", CultureInfo.InvariantCulture),
                DateTimeOffset dateTimeOffset => dateTimeOffset.ToString("O",
                    CultureInfo.InvariantCulture),
                IFormattable formattable => formattable.ToString(null,
                    CultureInfo.InvariantCulture),
                _ => scalarValue.Value.ToString() ?? string.Empty,
            };
        }

        private static LogEntryKind ParseKind(string value)
        {
            if (string.Equals(value, "Operation", StringComparison.OrdinalIgnoreCase))
            {
                return LogEntryKind.Operation;
            }

            if (string.Equals(value, "Diagnostic", StringComparison.OrdinalIgnoreCase))
            {
                return LogEntryKind.Diagnostic;
            }

            return LogEntryKind.Unclassified;
        }

        private static string Truncate(string value, int maximumLength, out bool truncated)
        {
            truncated = !string.IsNullOrEmpty(value) && value.Length > maximumLength;
            if (!truncated)
            {
                return value;
            }

            return string.Concat(value.AsSpan(0, maximumLength), "…");
        }

        private static LogLevel MapLevel(LogEventLevel level)
        {
            return level switch
            {
                LogEventLevel.Verbose => LogLevel.Verbose,
                LogEventLevel.Debug => LogLevel.Debug,
                LogEventLevel.Information => LogLevel.Information,
                LogEventLevel.Warning => LogLevel.Warning,
                LogEventLevel.Error => LogLevel.Error,
                LogEventLevel.Fatal => LogLevel.Fatal,
                _ => LogLevel.Information,
            };
        }

        /// <summary>
        /// Minimal Serilog sink that forwards each event to the owning service.
        /// Kept as a private nested type so the contract stays free of Serilog types.
        /// </summary>
        private sealed class LogHistorySink : ILogEventSink
        {
            private readonly LogHistoryService _owner;

            public LogHistorySink(LogHistoryService owner)
            {
                _owner = owner;
            }

            public void Emit(LogEvent logEvent) => _owner.Capture(logEvent);
        }
    }
}

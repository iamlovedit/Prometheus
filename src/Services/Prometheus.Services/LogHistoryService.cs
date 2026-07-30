using Prometheus.Core.Models;
using Prometheus.Services.Interfaces;
using Serilog.Core;
using Serilog.Events;

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
            if (logEvent is null)
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
            lock (_sync)
            {
                Array.Clear(_buffer, 0, _buffer.Length);
                _head = 0;
                _count = 0;
            }

            Cleared?.Invoke(this, EventArgs.Empty);
        }

        private static LogEntry ToEntry(LogEvent logEvent)
        {
            return new LogEntry(
                logEvent.Timestamp,
                MapLevel(logEvent.Level),
                logEvent.RenderMessage(),
                logEvent.Exception?.ToString());
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

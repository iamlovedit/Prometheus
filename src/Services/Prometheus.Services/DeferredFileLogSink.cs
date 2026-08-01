using Serilog;
using Serilog.Core;
using Serilog.Events;
using Serilog.Formatting;

namespace Prometheus.Services
{
    /// <summary>
    /// Creates the physical file sink only when the first accepted event is emitted. This keeps
    /// logging-disabled application sessions from creating empty log directories or files.
    /// </summary>
    public sealed class DeferredFileLogSink : ILogEventSink, IDisposable
    {
        private readonly object _sync = new();
        private readonly string _path;
        private readonly ITextFormatter _formatter;
        private readonly RollingInterval _rollingInterval;
        private readonly int? _retainedFileCountLimit;
        private readonly TimeSpan? _retainedFileTimeLimit;
        private Logger _logger;
        private bool _disposed;

        public DeferredFileLogSink(
            string path,
            ITextFormatter formatter,
            RollingInterval rollingInterval,
            int? retainedFileCountLimit,
            TimeSpan? retainedFileTimeLimit = null)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                throw new ArgumentException("A log file path is required.", nameof(path));
            }

            _path = path;
            _formatter = formatter ?? throw new ArgumentNullException(nameof(formatter));
            _rollingInterval = rollingInterval;
            _retainedFileCountLimit = retainedFileCountLimit;
            _retainedFileTimeLimit = retainedFileTimeLimit;
        }

        public void Emit(LogEvent logEvent)
        {
            ArgumentNullException.ThrowIfNull(logEvent);

            lock (_sync)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                _logger ??= CreateLogger();
                _logger.Write(logEvent);
            }
        }

        public void Dispose()
        {
            lock (_sync)
            {
                if (_disposed)
                {
                    return;
                }

                _disposed = true;
                _logger?.Dispose();
                _logger = null;
            }
        }

        private Logger CreateLogger()
        {
            var directory = Path.GetDirectoryName(_path);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            return new LoggerConfiguration()
                .MinimumLevel.Verbose()
                .WriteTo.File(
                    _formatter,
                    _path,
                    rollingInterval: _rollingInterval,
                    retainedFileCountLimit: _retainedFileCountLimit,
                    retainedFileTimeLimit: _retainedFileTimeLimit)
                .CreateLogger();
        }
    }
}

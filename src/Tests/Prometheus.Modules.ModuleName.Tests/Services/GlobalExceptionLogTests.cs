using Prometheus.Core.Logging;
using Prometheus.Core.Models;
using Prometheus.Services;
using Serilog;
using Serilog.Events;
using System.Runtime.CompilerServices;
using Xunit;

namespace Prometheus.Modules.ModuleName.Tests.Services
{
    public class GlobalExceptionLogTests
    {
        [Fact]
        public void Write_CapturesStructuredDiagnosticWithoutRawExceptionMessageOrFilePath()
        {
            var history = new LogHistoryService(10);
            using var logger = new LoggerConfiguration()
                .WriteTo.Sink(history.Sink)
                .CreateLogger();
            var exception = CaptureSensitiveException();

            GlobalExceptionLog.Write(
                logger,
                LogEventLevel.Fatal,
                "application.exception.ui.unhandled",
                "Dispatcher",
                exception,
                isTerminating: true,
                "Unhandled UI thread exception");

            var entry = Assert.Single(history.GetSnapshot());
            Assert.Equal(LogLevel.Fatal, entry.Level);
            Assert.Equal(LogEntryKind.Diagnostic, entry.Kind);
            Assert.Equal("application.exception.ui.unhandled", entry.EventName);
            Assert.Equal("Diagnostics", entry.Category);
            Assert.Equal("System", entry.Origin);
            Assert.Equal(typeof(InvalidOperationException).FullName,
                entry.GetPropertyValue("ErrorType"));
            Assert.Equal("Dispatcher", entry.GetPropertyValue("ExceptionBoundary"));
            Assert.Equal("True", entry.GetPropertyValue("IsTerminating"));
            Assert.Contains(nameof(ThrowSensitiveException),
                entry.GetPropertyValue("SafeStackTrace"));
            Assert.Null(entry.Exception);

            var capturedText = string.Join(
                Environment.NewLine,
                entry.Message,
                entry.Exception ?? string.Empty,
                string.Join(Environment.NewLine,
                    entry.Properties.Select(property => property.Value)));
            Assert.DoesNotContain("super-secret-token", capturedText,
                StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("C:\\private\\player-data.json", capturedText,
                StringComparison.OrdinalIgnoreCase);
        }

        private static Exception CaptureSensitiveException()
        {
            try
            {
                ThrowSensitiveException();
                throw new InvalidOperationException("The test exception was not thrown.");
            }
            catch (Exception exception)
            {
                return exception;
            }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void ThrowSensitiveException()
        {
            throw new InvalidOperationException(
                "super-secret-token at C:\\private\\player-data.json");
        }
    }
}

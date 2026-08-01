using Prometheus.Services;
using Serilog;
using Serilog.Formatting.Json;
using System.Configuration;
using System.Reflection;
using Xunit;

namespace Prometheus.Modules.ModuleName.Tests.Services
{
    public class LoggingControlServiceTests
    {
        [Fact]
        public void DesktopSetting_DefaultsToDisabled()
        {
            var settingsType = typeof(Prometheus.App).Assembly
                .GetType("Prometheus.Properties.Settings", throwOnError: true);
            var enableLogging = settingsType!.GetProperty(
                "EnableLogging",
                BindingFlags.Instance | BindingFlags.Public);
            var defaultValue = enableLogging!.GetCustomAttribute<DefaultSettingValueAttribute>();

            Assert.Equal("False", defaultValue?.Value);
        }

        [Fact]
        public void RuntimeGate_BlocksAllSinksUntilEnabledAndClearsMemoryWhenDisabled()
        {
            var testDirectory = Path.Combine(
                Path.GetTempPath(),
                "Prometheus.LoggingControlServiceTests",
                Guid.NewGuid().ToString("N"));
            var logPath = Path.Combine(testDirectory, "Logs", "prometheus-.jsonl");
            var history = new LogHistoryService(20);
            var persistedValues = new List<bool>();
            var control = new LoggingControlService(
                false,
                history,
                persistedValues.Add);

            try
            {
                using var logger = new LoggerConfiguration()
                    .MinimumLevel.Verbose()
                    .Filter.With(control)
                    .WriteTo.Sink(new LoggingControlledSink(
                        control,
                        new DeferredFileLogSink(
                            logPath,
                            new JsonFormatter(renderMessage: true),
                            RollingInterval.Day,
                            retainedFileCountLimit: 2)))
                    .WriteTo.Sink(new LoggingControlledSink(control, history.Sink))
                    .CreateLogger();

                logger.Fatal("Blocked while logging is disabled");

                Assert.Empty(history.GetSnapshot());
                Assert.False(Directory.Exists(testDirectory));

                control.SetEnabled(true);
                logger.Information("Accepted after logging was enabled");

                Assert.Single(history.GetSnapshot());
                var file = Assert.Single(Directory.EnumerateFiles(
                    Path.Combine(testDirectory, "Logs"),
                    "*.jsonl"));
                var enabledLength = new FileInfo(file).Length;
                Assert.True(enabledLength > 0);

                control.SetEnabled(false);
                logger.Fatal("Blocked after logging was disabled again");

                Assert.Empty(history.GetSnapshot());
                Assert.Equal(enabledLength, new FileInfo(file).Length);
                Assert.Equal(new[] { true, false }, persistedValues);
            }
            finally
            {
                if (Directory.Exists(testDirectory))
                {
                    Directory.Delete(testDirectory, recursive: true);
                }
            }
        }
    }
}

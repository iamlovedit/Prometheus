using Prometheus.Core.Logging;
using Prometheus.Core.Models;
using Prometheus.Services;
using Serilog;
using Serilog.Core;
using Serilog.Events;
using Xunit;

namespace Prometheus.Modules.ModuleName.Tests.Services
{
    [CollectionDefinition(CollectionName, DisableParallelization = true)]
    public sealed class GlobalSerilogCollection
    {
        public const string CollectionName = "Global Serilog";
    }

    [Collection(GlobalSerilogCollection.CollectionName)]
    public class LogHistoryServiceTests
    {
        [Fact]
        public void Capture_PreservesStructuredFieldsAndBoundsLargePropertyPreview()
        {
            var history = new LogHistoryService(10);
            using var logger = new LoggerConfiguration()
                .WriteTo.Sink(history.Sink)
                .CreateLogger();
            var operationId = Guid.NewGuid();
            var eventId = Guid.NewGuid();
            var sessionId = Guid.NewGuid();
            var largeTargetId = new string('x', 10000);

            logger.ForContext("Kind", "Operation")
                .ForContext("EventName", "match.ready_check.accept")
                .ForContext("Category", "Match")
                .ForContext("Origin", "Manual")
                .ForContext("Outcome", "Succeeded")
                .ForContext("Module", "Home")
                .ForContext("EventId", eventId)
                .ForContext("OperationId", operationId)
                .ForContext("AppSessionId", sessionId)
                .ForContext("DurationMs", 126)
                .ForContext("TargetId", largeTargetId)
                .Information("Accepted the ready check");

            var entry = Assert.Single(history.GetSnapshot());
            Assert.Equal(LogEntryKind.Operation, entry.Kind);
            Assert.Equal("match.ready_check.accept", entry.EventName);
            Assert.Equal("Match", entry.Category);
            Assert.Equal("Manual", entry.Origin);
            Assert.Equal("Succeeded", entry.Outcome);
            Assert.Equal("Home", entry.Module);
            Assert.Equal(eventId.ToString(), entry.EventId);
            Assert.Equal(operationId.ToString(), entry.OperationId);
            Assert.Equal(sessionId.ToString(), entry.AppSessionId);
            Assert.Equal("126", entry.DurationMs);

            var targetId = Assert.Single(entry.Properties,
                property => property.Name == "TargetId");
            Assert.True(targetId.IsTruncated);
            Assert.Equal(4097, targetId.Value.Length);
            Assert.DoesNotContain(largeTargetId, entry.Message, StringComparison.Ordinal);
        }

        [Fact]
        public void Capture_OnlyRetainsBoundedDataForSanitizedWebsocketEvents()
        {
            var history = new LogHistoryService(10);
            using var logger = new LoggerConfiguration()
                .WriteTo.Sink(history.Sink)
                .CreateLogger();
            var largeData = new string('x', 10000);

            logger.ForContext("Data", "generic-data")
                .Information("Generic event");
            logger.ForContext("Kind", "Diagnostic")
                .ForContext("EventName", "lcu.websocket.event.received")
                .ForContext("Category", "WebSocket")
                .ForContext("Origin", "Observed")
                .ForContext("Data", largeData)
                .Information("Received websocket event");

            var entries = history.GetSnapshot();
            Assert.Equal(2, entries.Count);
            Assert.DoesNotContain(entries[0].Properties, property => property.Name == "Data");
            var data = Assert.Single(entries[1].Properties,
                property => property.Name == "Data");
            Assert.True(data.IsTruncated);
            Assert.Equal(4097, data.Value.Length);
        }

        [Fact]
        public void Capture_WhenEventSkipsMemory_DoesNotStoreEntry()
        {
            var history = new LogHistoryService(10);
            using var logger = new LoggerConfiguration()
                .WriteTo.Sink(history.Sink)
                .CreateLogger();

            logger.ForContext("SkipInMemoryLog", true)
                .Information("Persistent-only event");

            Assert.Empty(history.GetSnapshot());
        }

        [Fact]
        public void Clear_LeavesPanelEmptyAndWritesPersistentClearEvent()
        {
            var history = new LogHistoryService(10);
            var persistentSink = new CollectingSink();
            var previousLogger = Log.Logger;
            using var logger = new LoggerConfiguration()
                .WriteTo.Sink(history.Sink)
                .WriteTo.Sink(persistentSink)
                .CreateLogger();
            Log.Logger = logger;

            try
            {
                Log.Information("Before clear");
                history.Clear();

                Assert.Empty(history.GetSnapshot());
                var clearEvent = Assert.Single(persistentSink.Events, logEvent =>
                    TryGetScalar<string>(logEvent, "EventName", out var eventName)
                    && eventName == "diagnostics.logs.clear");
                Assert.Equal("Operation", GetScalar<string>(clearEvent, "Kind"));
                Assert.Equal("Succeeded", GetScalar<string>(clearEvent, "Outcome"));
                Assert.Equal(1, GetScalar<int>(clearEvent, "PreviousCount"));
                Assert.Equal("CurrentSessionMemory",
                    GetScalar<string>(clearEvent, "ClearScope"));
                Assert.True(GetScalar<bool>(clearEvent, "SkipInMemoryLog"));
            }
            finally
            {
                Log.Logger = previousLogger;
            }
        }

        [Fact]
        public void OperationLog_OnlyWritesWhitelistedExtendedProperties()
        {
            var sink = new CollectingSink();
            var previousLogger = Log.Logger;
            using var logger = new LoggerConfiguration()
                .WriteTo.Sink(sink)
                .CreateLogger();
            Log.Logger = logger;

            try
            {
                OperationLog.Write(
                    LogEventLevel.Information,
                    "profile.background.changed",
                    "Profile",
                    "Manual",
                    "Succeeded",
                    Guid.NewGuid(),
                    "Tests",
                    "Background changed",
                    new Dictionary<string, object>
                    {
                        ["SkinId"] = 12345,
                        ["UnsafeDynamicPayload"] = "must-not-be-logged",
                    });

                var logEvent = Assert.Single(sink.Events);
                Assert.Equal(12345, GetScalar<int>(logEvent, "SkinId"));
                Assert.False(logEvent.Properties.ContainsKey("UnsafeDynamicPayload"));
            }
            finally
            {
                Log.Logger = previousLogger;
            }
        }

        private static T GetScalar<T>(LogEvent logEvent, string propertyName)
        {
            var scalar = Assert.IsType<ScalarValue>(logEvent.Properties[propertyName]);
            return Assert.IsType<T>(scalar.Value);
        }

        private static bool TryGetScalar<T>(
            LogEvent logEvent,
            string propertyName,
            out T value)
        {
            value = default;
            if (!logEvent.Properties.TryGetValue(propertyName, out var propertyValue)
                || propertyValue is not ScalarValue { Value: T typedValue })
            {
                return false;
            }

            value = typedValue;
            return true;
        }

        private sealed class CollectingSink : ILogEventSink
        {
            public List<LogEvent> Events { get; } = [];

            public void Emit(LogEvent logEvent)
            {
                Events.Add(logEvent);
            }
        }
    }
}

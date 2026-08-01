using Prometheus.Core.Models;
using Prometheus.Modules.Setting.ViewModels;
using Xunit;

namespace Prometheus.Modules.ModuleName.Tests.ViewModels
{
    public class LogSearchQueryTests
    {
        [Fact]
        public void Matches_CombinesStableFieldsAndFreeText()
        {
            var entry = CreateEntry(
                DateTimeOffset.Now,
                LogLevel.Error,
                LogEntryKind.Operation,
                "match.reconnect",
                "Match",
                "Automation",
                "Failed",
                "Automatic reconnect timed out",
                [new LogEntryProperty("AttemptCount", "3")]);
            var query = LogSearchQuery.Parse(
                "kind:operation category:match origin:automation outcome:failed timed");

            Assert.True(query.Matches(entry, DateTimeOffset.Now));
            Assert.False(LogSearchQuery.Parse("outcome:succeeded")
                .Matches(entry, DateTimeOffset.Now));
        }

        [Fact]
        public void Matches_SupportsEventPrefixesMinimumLevelsAndTimeRanges()
        {
            var now = DateTimeOffset.Now;
            var recent = CreateEntry(
                now.AddMinutes(-10),
                LogLevel.Warning,
                LogEntryKind.Diagnostic,
                "lcu.websocket.event.received",
                "WebSocket",
                "Observed",
                null,
                "Received event");
            var old = CreateEntry(
                now.AddHours(-2),
                LogLevel.Warning,
                LogEntryKind.Diagnostic,
                "lcu.websocket.event.received",
                "WebSocket",
                "Observed",
                null,
                "Received event");
            var query = LogSearchQuery.Parse(
                "event:lcu.websocket.* level:warning+ after:30m");

            Assert.True(query.Matches(recent, now));
            Assert.False(query.Matches(old, now));
        }

        [Fact]
        public void Matches_SearchesRetainedPropertyNamesAndValues()
        {
            var entry = CreateEntry(
                DateTimeOffset.Now,
                LogLevel.Information,
                LogEntryKind.Operation,
                "profile.background.changed",
                "Profile",
                "Manual",
                "Succeeded",
                "Background changed",
                [new LogEntryProperty("SkinId", "12345")]);

            Assert.True(LogSearchQuery.Parse("SkinId 12345")
                .Matches(entry, DateTimeOffset.Now));
        }

        private static LogEntry CreateEntry(
            DateTimeOffset timestamp,
            LogLevel level,
            LogEntryKind kind,
            string eventName,
            string category,
            string origin,
            string outcome,
            string message,
            IReadOnlyList<LogEntryProperty> properties = null)
        {
            return new LogEntry(
                timestamp,
                level,
                message,
                null,
                kind,
                eventName,
                category,
                origin,
                outcome,
                "Tests",
                Guid.NewGuid().ToString(),
                Guid.NewGuid().ToString(),
                Guid.NewGuid().ToString(),
                properties ?? [],
                false,
                false);
        }
    }
}

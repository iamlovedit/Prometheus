using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Moq;
using Prometheus.Services.Client;
using Prometheus.Services.Interfaces.Client;
using Serilog;
using Serilog.Core;
using Serilog.Events;
using Xunit;

namespace Prometheus.Modules.ModuleName.Tests.Services
{
    public class LeagueClientTests
    {
        [Fact]
        public async Task StartAsync_WhenClientUnavailable_CanStopAndRestart()
        {
            var clientService = new Mock<IClientService>();
            clientService.Setup(service => service.GetClientProcessId()).Returns(0);
            clientService.Setup(service => service.GetClientCommandLines()).Returns((System.Collections.Generic.Dictionary<string, string>)null);
            var client = new LeagueClient(clientService.Object);

            Assert.False(await client.StartAsync());
            Assert.False(client.Connected);
            await client.StopAsync();

            Assert.False(await client.StartAsync());
            Assert.False(client.Connected);
            await client.StopAsync();
        }

        [Fact]
        public void Sanitize_PreservesSafeDataAndRedactsSensitiveDataRecursively()
        {
            const string token = "lcu-secret-token-123456789";
            var data = new JObject
            {
                ["championId"] = 266,
                ["queueId"] = 420,
                ["id"] = 987654321,
                ["flags"] = new JArray(true, false),
                ["accessToken"] = token,
                ["misc"] = $"prefix-{token}-suffix",
                ["displayName"] = "Private Player",
                ["puuid"] = "private-puuid-value",
                ["summonerId"] = 123456789L,
                ["message"] = "private chat text",
                ["filePath"] = @"C:\Users\Player\secret.txt",
                ["uri"] = "/lol-summoner/v2/summoners/123456789?includePrivate=true",
                ["metadata"] = "{\"accessToken\":\"nested-secret\",\"queueId\":450}",
                ["malformedMetadata"] = "{\"password\":\"private-value\"",
                ["opaqueValue"] = "abcdefghijklmnopqrstuvwxyz0123456789ABCDEFGHIJ",
                ["participants"] = new JArray
                {
                    new JObject
                    {
                        ["id"] = "private-participant-id",
                        ["championId"] = 103,
                    },
                },
            };

            var result = WebsocketEventLogSanitizer.Sanitize(data, token);

            Assert.False(result.Failed);
            Assert.True(result.RedactedFieldCount >= 12);
            var sanitized = JObject.Parse(result.Data);
            Assert.Equal(266, sanitized["championId"]?.Value<int>());
            Assert.Equal(420, sanitized["queueId"]?.Value<int>());
            Assert.Equal(WebsocketEventLogSanitizer.RedactedValue,
                sanitized["id"]?.Value<string>());
            Assert.Equal(2, sanitized["flags"]?.Count());
            Assert.Equal(WebsocketEventLogSanitizer.RedactedValue,
                sanitized["accessToken"]?.Value<string>());
            Assert.Equal(WebsocketEventLogSanitizer.RedactedValue,
                sanitized["misc"]?.Value<string>());
            Assert.Equal(WebsocketEventLogSanitizer.RedactedValue,
                sanitized["displayName"]?.Value<string>());
            Assert.Equal(WebsocketEventLogSanitizer.RedactedValue,
                sanitized["puuid"]?.Value<string>());
            Assert.Equal(WebsocketEventLogSanitizer.RedactedValue,
                sanitized["summonerId"]?.Value<string>());
            Assert.Equal(WebsocketEventLogSanitizer.RedactedValue,
                sanitized["message"]?.Value<string>());
            Assert.Equal(WebsocketEventLogSanitizer.RedactedValue,
                sanitized["filePath"]?.Value<string>());
            Assert.Equal("/lol-summoner/v2/summoners/[REDACTED]",
                sanitized["uri"]?.Value<string>());
            Assert.Equal(WebsocketEventLogSanitizer.RedactedValue,
                sanitized["participants"]?[0]?["id"]?.Value<string>());
            Assert.Equal(103, sanitized["participants"]?[0]?["championId"]?.Value<int>());

            var nested = JObject.Parse(sanitized["metadata"]?.Value<string>() ?? string.Empty);
            Assert.Equal(WebsocketEventLogSanitizer.RedactedValue,
                nested["accessToken"]?.Value<string>());
            Assert.Equal(450, nested["queueId"]?.Value<int>());
            Assert.Equal(WebsocketEventLogSanitizer.RedactedValue,
                sanitized["malformedMetadata"]?.Value<string>());
            Assert.Equal(WebsocketEventLogSanitizer.RedactedValue,
                sanitized["opaqueValue"]?.Value<string>());

            Assert.DoesNotContain(token, result.Data, StringComparison.Ordinal);
            Assert.DoesNotContain("Private Player", result.Data, StringComparison.Ordinal);
            Assert.DoesNotContain("private chat text", result.Data, StringComparison.Ordinal);
            Assert.DoesNotContain(@"C:\Users\Player", result.Data, StringComparison.Ordinal);
            Assert.Equal(token, data["accessToken"]?.Value<string>());
        }

        [Fact]
        public void Sanitize_WhenSerializationFails_ReturnsSafeUnavailableValue()
        {
            var data = new SelfReferencingData();
            data.Self = data;

            var result = WebsocketEventLogSanitizer.Sanitize(data);

            Assert.True(result.Failed);
            Assert.Equal(WebsocketEventLogSanitizer.UnavailableValue, result.Data);
            Assert.Equal(nameof(JsonSerializationException), result.ErrorType);
        }

        [Fact]
        public void HandleMessageReceived_LogsSanitizedEventBeforeDispatch()
        {
            const string token = "lcu-secret-token-123456789";
            var clientService = new Mock<IClientService>();
            var sink = new CollectingSink();
            using var logger = new LoggerConfiguration()
                .MinimumLevel.Verbose()
                .WriteTo.Sink(sink)
                .CreateLogger();
            var client = new LeagueClient(clientService.Object, logger)
            {
                Token = token,
            };
            var subscriberInvoked = false;
            var observerInvoked = false;
            const string eventUri =
                "/lol-chat/v1/conversations/private-conversation/messages?token=lcu-secret-token-123456789";
            client.OnWebsocketEvent += _ =>
            {
                Assert.Single(sink.Events);
                observerInvoked = true;
            };
            client.Subscribe(eventUri, _ =>
            {
                Assert.Single(sink.Events);
                subscriberInvoked = true;
            });
            var payload = new JArray
            {
                8,
                "OnJsonApiEvent",
                new JObject
                {
                    ["eventType"] = "Update",
                    ["uri"] = eventUri,
                    ["data"] = new JObject
                    {
                        ["championId"] = 99,
                        ["displayName"] = "Private Player",
                        ["unknownField"] = token,
                    },
                },
            };

            client.HandleMessageReceived(payload.ToString(Formatting.None));

            Assert.True(subscriberInvoked);
            Assert.True(observerInvoked);
            var logEvent = Assert.Single(sink.Events);
            Assert.Equal("Diagnostic", GetScalar<string>(logEvent, "Kind"));
            Assert.Equal("lcu.websocket.event.received",
                GetScalar<string>(logEvent, "EventName"));
            Assert.Equal("WebSocket", GetScalar<string>(logEvent, "Category"));
            Assert.Equal("Observed", GetScalar<string>(logEvent, "Origin"));
            Assert.Equal("Update", GetScalar<string>(logEvent, "EventType"));
            Assert.Equal("/lol-chat/v1/conversations/[REDACTED]/messages",
                GetScalar<string>(logEvent, "Uri"));
            Assert.False(GetScalar<bool>(logEvent, "DataSanitizationFailed"));
            Assert.True(GetScalar<int>(logEvent, "DataRedactedFieldCount") >= 2);

            var loggedDataText = GetScalar<string>(logEvent, "Data");
            var loggedData = JObject.Parse(loggedDataText);
            Assert.Equal(99, loggedData["championId"]?.Value<int>());
            Assert.Equal(WebsocketEventLogSanitizer.RedactedValue,
                loggedData["displayName"]?.Value<string>());
            Assert.Equal(WebsocketEventLogSanitizer.RedactedValue,
                loggedData["unknownField"]?.Value<string>());
            Assert.DoesNotContain(token, logEvent.RenderMessage(), StringComparison.Ordinal);
            Assert.DoesNotContain("Private Player", logEvent.RenderMessage(),
                StringComparison.Ordinal);
        }

        [Fact]
        public void HandleMessageReceived_WhenNoUriSubscriber_StillLogsEventOnce()
        {
            var clientService = new Mock<IClientService>();
            var sink = new CollectingSink();
            using var logger = new LoggerConfiguration()
                .WriteTo.Sink(sink)
                .CreateLogger();
            var client = new LeagueClient(clientService.Object, logger);
            var payload = new JArray
            {
                8,
                "OnJsonApiEvent",
                new JObject
                {
                    ["eventType"] = "Create",
                    ["uri"] = "/lol-ranked/v1/notifications",
                    ["data"] = new JArray(1, 2, 3),
                },
            };

            client.HandleMessageReceived(payload.ToString(Formatting.None));

            var logEvent = Assert.Single(sink.Events);
            Assert.Equal("lcu.websocket.event.received",
                GetScalar<string>(logEvent, "EventName"));
            Assert.Equal("[1,2,3]", GetScalar<string>(logEvent, "Data"));
        }

        private static T GetScalar<T>(LogEvent logEvent, string propertyName)
        {
            var scalar = Assert.IsType<ScalarValue>(logEvent.Properties[propertyName]);
            return Assert.IsType<T>(scalar.Value);
        }

        private sealed class CollectingSink : ILogEventSink
        {
            public List<LogEvent> Events { get; } = [];

            public void Emit(LogEvent logEvent)
            {
                Events.Add(logEvent);
            }
        }

        private sealed class SelfReferencingData
        {
            public SelfReferencingData Self { get; set; }
        }
    }
}

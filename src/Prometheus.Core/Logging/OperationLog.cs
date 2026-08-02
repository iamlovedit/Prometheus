using Serilog;
using Serilog.Events;

namespace Prometheus.Core.Logging
{
    /// <summary>
    /// Writes privacy-reviewed business operation events with the stable
    /// correlation fields required by the operation-log specification.
    /// </summary>
    public static class OperationLog
    {
        private const int MaximumStringPropertyLength = 2048;

        private static readonly HashSet<string> AllowedPropertyNames = new(
            StringComparer.Ordinal)
        {
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
            "QueueId",
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
            "SkipInMemoryLog",
        };

        private static readonly Guid _appSessionId = Guid.NewGuid();

        public static void Write(
            LogEventLevel level,
            string eventName,
            string category,
            string origin,
            string outcome,
            Guid operationId,
            string module,
            string displayMessage,
            IReadOnlyDictionary<string, object> properties = null,
            Exception exception = null)
        {
            var logger = Log.ForContext("Kind", "Operation")
                .ForContext("EventName", eventName)
                .ForContext("Category", category)
                .ForContext("Origin", origin)
                .ForContext("Outcome", outcome)
                .ForContext("EventId", Guid.NewGuid())
                .ForContext("OperationId", operationId)
                .ForContext("AppSessionId", _appSessionId)
                .ForContext("Module", module);

            if (properties is not null)
            {
                foreach (var property in properties)
                {
                    if (!AllowedPropertyNames.Contains(property.Key))
                    {
                        continue;
                    }

                    var value = property.Value is string text
                        && text.Length > MaximumStringPropertyLength
                            ? string.Concat(text.AsSpan(0, MaximumStringPropertyLength), "…")
                            : property.Value;
                    logger = logger.ForContext(property.Key, value);
                }
            }

            logger.Write(level, exception, "{DisplayMessage}", displayMessage);
        }
    }
}

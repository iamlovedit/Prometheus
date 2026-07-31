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
                    logger = logger.ForContext(property.Key, property.Value);
                }
            }

            logger.Write(level, exception, "{DisplayMessage}", displayMessage);
        }
    }
}

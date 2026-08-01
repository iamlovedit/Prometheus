using Serilog;
using Serilog.Events;
using System.Diagnostics;
using System.Text;

namespace Prometheus.Core.Logging
{
    /// <summary>
    /// Writes privacy-reviewed global exception diagnostics without persisting the raw exception
    /// message or source file paths.
    /// </summary>
    public static class GlobalExceptionLog
    {
        private const int MaximumExceptionCount = 16;
        private const int MaximumSafeStackTraceLength = 16384;

        public static void Write(
            ILogger logger,
            LogEventLevel level,
            string eventName,
            string boundary,
            object exceptionObject,
            bool isTerminating,
            string displayMessage)
        {
            ArgumentNullException.ThrowIfNull(logger);

            var errorType = exceptionObject?.GetType().FullName ?? "Unknown";
            var context = logger
                .ForContext("Kind", "Diagnostic")
                .ForContext("EventName", eventName)
                .ForContext("Category", "Diagnostics")
                .ForContext("Origin", "System")
                .ForContext("EventId", Guid.NewGuid())
                .ForContext("Module", "Application")
                .ForContext("ErrorType", errorType)
                .ForContext("ExceptionBoundary", boundary)
                .ForContext("IsTerminating", isTerminating);

            if (exceptionObject is Exception exception)
            {
                var exceptionDetails = BuildSafeExceptionDetails(exception);
                context = context
                    .ForContext("HResult", exception.HResult)
                    .ForContext("ExceptionCount", exceptionDetails.ExceptionCount);

                if (!string.IsNullOrWhiteSpace(exceptionDetails.SafeStackTrace))
                {
                    context = context.ForContext(
                        "SafeStackTrace",
                        exceptionDetails.SafeStackTrace);
                }
            }

            context.Write(level, "{DisplayMessage}", displayMessage);
        }

        private static SafeExceptionDetails BuildSafeExceptionDetails(Exception root)
        {
            var builder = new StringBuilder();
            var pending = new Queue<Exception>();
            var visited = new HashSet<Exception>(ReferenceEqualityComparer.Instance);
            pending.Enqueue(root);

            var exceptionCount = 0;
            while (pending.Count > 0 && exceptionCount < MaximumExceptionCount)
            {
                var exception = pending.Dequeue();
                if (!visited.Add(exception))
                {
                    continue;
                }

                if (builder.Length > 0)
                {
                    builder.AppendLine();
                }

                builder.Append('[')
                    .Append(exceptionCount)
                    .Append("] ")
                    .AppendLine(exception.GetType().FullName ?? exception.GetType().Name);
                var stackTrace = new StackTrace(exception, fNeedFileInfo: false).ToString();
                if (!string.IsNullOrWhiteSpace(stackTrace))
                {
                    builder.Append(stackTrace.TrimEnd());
                }

                exceptionCount++;
                if (exception is AggregateException aggregateException)
                {
                    foreach (var innerException in aggregateException.InnerExceptions)
                    {
                        pending.Enqueue(innerException);
                    }
                }
                else if (exception.InnerException is not null)
                {
                    pending.Enqueue(exception.InnerException);
                }

                if (builder.Length >= MaximumSafeStackTraceLength)
                {
                    break;
                }
            }

            var safeStackTrace = builder.ToString();
            if (safeStackTrace.Length > MaximumSafeStackTraceLength)
            {
                safeStackTrace = string.Concat(
                    safeStackTrace.AsSpan(0, MaximumSafeStackTraceLength),
                    "…");
            }

            return new SafeExceptionDetails(exceptionCount, safeStackTrace);
        }

        private readonly record struct SafeExceptionDetails(
            int ExceptionCount,
            string SafeStackTrace);
    }
}

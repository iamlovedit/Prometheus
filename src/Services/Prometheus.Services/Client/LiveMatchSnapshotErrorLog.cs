using Prometheus.Core.Models;
using Serilog;
using System.Diagnostics;
using System.Text;

namespace Prometheus.Services.Client
{
    /// <summary>
    /// Writes privacy-reviewed diagnostics when the published live-match snapshot enters a new
    /// error state. Raw exception messages are intentionally excluded because they may contain
    /// LCU URLs, credentials, player identifiers, or local paths.
    /// </summary>
    internal static class LiveMatchSnapshotErrorLog
    {
        private const int MaximumExceptionCount = 16;
        private const int MaximumSafeErrorLength = 2048;
        private const int MaximumSafeStackTraceLength = 16384;
        private const string DefaultSafeError = "Live-match snapshot entered an error state.";

        internal static void Write(
            ILogger logger,
            LiveMatchSnapshot snapshot,
            string safeError,
            IReadOnlyList<Exception> exceptions = null)
        {
            ArgumentNullException.ThrowIfNull(logger);
            ArgumentNullException.ThrowIfNull(snapshot);

            var normalizedExceptions = exceptions?
                .Where(exception => exception is not null)
                .ToArray() ?? Array.Empty<Exception>();
            var normalizedError = NormalizeSafeError(safeError);
            var safeStackTrace = BuildSafeStackTrace(normalizedExceptions);
            var errorTypes = normalizedExceptions.Length == 0
                ? "SnapshotState"
                : string.Join(", ", normalizedExceptions
                    .Select(exception => exception.GetType().FullName ?? exception.GetType().Name)
                    .Distinct(StringComparer.Ordinal));

            logger
                .ForContext("Kind", "Diagnostic")
                .ForContext("EventName", "match.snapshot.error")
                .ForContext("Category", "Match")
                .ForContext("Origin", "Observed")
                .ForContext("EventId", Guid.NewGuid())
                .ForContext("Module", "MatchService")
                .ForContext("SnapshotVersion", snapshot.Version)
                .ForContext("ConnectionState", snapshot.ConnectionState.ToString())
                .ForContext("GameflowPhase", snapshot.GameflowPhase.ToString())
                .ForContext("ErrorType", errorTypes)
                .ForContext("ExceptionCount", normalizedExceptions.Length)
                .ForContext("CallStackKind",
                    normalizedExceptions.Length == 0 ? "Publication" : "Exception")
                .ForContext("SafeStackTrace", safeStackTrace)
                .Error("Live-match snapshot error: {SnapshotError}", normalizedError);
        }

        internal static string SanitizeStateError(string error, params string[] knownSecrets)
        {
            if (string.IsNullOrWhiteSpace(error))
            {
                return DefaultSafeError;
            }

            var sanitized = WebsocketEventLogSanitizer.SanitizeScalar(error, knownSecrets);
            if (string.Equals(sanitized, WebsocketEventLogSanitizer.RedactedValue,
                    StringComparison.Ordinal) ||
                sanitized.Contains("://", StringComparison.Ordinal) ||
                ContainsAbsoluteLocalPath(sanitized))
            {
                return DefaultSafeError;
            }

            return NormalizeSafeError(sanitized);
        }

        private static string NormalizeSafeError(string safeError)
        {
            var value = string.IsNullOrWhiteSpace(safeError)
                ? DefaultSafeError
                : safeError.Trim();
            return value.Length <= MaximumSafeErrorLength
                ? value
                : string.Concat(value.AsSpan(0, MaximumSafeErrorLength), "…");
        }

        private static string BuildSafeStackTrace(IReadOnlyList<Exception> exceptions)
        {
            if (exceptions.Count == 0)
            {
                return TruncateStackTrace(new StackTrace(skipFrames: 2,
                    fNeedFileInfo: false).ToString());
            }

            var builder = new StringBuilder();
            var pending = new Queue<Exception>(exceptions);
            var visited = new HashSet<Exception>(ReferenceEqualityComparer.Instance);
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

            if (builder.Length == 0 || exceptions.All(exception =>
                    string.IsNullOrWhiteSpace(exception.StackTrace)))
            {
                builder.AppendLine()
                    .AppendLine("[publication]")
                    .Append(new StackTrace(skipFrames: 2, fNeedFileInfo: false));
            }

            return TruncateStackTrace(builder.ToString());
        }

        private static string TruncateStackTrace(string stackTrace)
        {
            if (string.IsNullOrWhiteSpace(stackTrace))
            {
                return "Stack trace unavailable.";
            }

            return stackTrace.Length <= MaximumSafeStackTraceLength
                ? stackTrace
                : string.Concat(stackTrace.AsSpan(0, MaximumSafeStackTraceLength), "…");
        }

        private static bool ContainsAbsoluteLocalPath(string value)
        {
            for (var index = 0; index + 2 < value.Length; index++)
            {
                if (char.IsLetter(value[index]) && value[index + 1] == ':' &&
                    value[index + 2] is '\\' or '/')
                {
                    return true;
                }
            }

            return value.Contains("\\\\", StringComparison.Ordinal);
        }
    }
}

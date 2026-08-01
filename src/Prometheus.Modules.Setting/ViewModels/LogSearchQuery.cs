using Prometheus.Core.Models;
using System.Globalization;
using System.Text.RegularExpressions;

namespace Prometheus.Modules.Setting.ViewModels
{
    /// <summary>
    /// Parses the compact query syntax used by the log search box. Structured terms are matched
    /// against stable event fields; unqualified terms search the message, exception and retained
    /// privacy-reviewed properties.
    /// </summary>
    public sealed class LogSearchQuery
    {
        private static readonly Regex TokenPattern = new(
            """(?:(?<key>[A-Za-z]+):(?:"(?<value>[^"]*)"|(?<value>\S+))|"(?<term>[^"]+)"|(?<term>\S+))""",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        private readonly IReadOnlyList<QueryCondition> _conditions;
        private readonly IReadOnlyList<string> _terms;

        private LogSearchQuery(
            IReadOnlyList<QueryCondition> conditions,
            IReadOnlyList<string> terms)
        {
            _conditions = conditions;
            _terms = terms;
        }

        public static LogSearchQuery Empty { get; } = new([], []);

        public static LogSearchQuery Parse(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return Empty;
            }

            var conditions = new List<QueryCondition>();
            var terms = new List<string>();
            foreach (System.Text.RegularExpressions.Match match in TokenPattern.Matches(text))
            {
                var key = match.Groups["key"].Value;
                var value = match.Groups["value"].Value;
                if (!string.IsNullOrWhiteSpace(key)
                    && IsSupportedKey(key)
                    && !string.IsNullOrWhiteSpace(value))
                {
                    if (string.Equals(key, "after", StringComparison.OrdinalIgnoreCase)
                        && !TryParseDuration(value, out var duration))
                    {
                        terms.Add(match.Value);
                        continue;
                    }

                    conditions.Add(new QueryCondition(key, value));
                    continue;
                }

                var term = match.Groups["term"].Value;
                if (!string.IsNullOrWhiteSpace(term))
                {
                    terms.Add(term);
                }
            }

            return conditions.Count == 0 && terms.Count == 0
                ? Empty
                : new LogSearchQuery(conditions, terms);
        }

        public bool Matches(LogEntry entry, DateTimeOffset now)
        {
            ArgumentNullException.ThrowIfNull(entry);

            foreach (var condition in _conditions)
            {
                if (!MatchesCondition(entry, condition, now))
                {
                    return false;
                }
            }

            return _terms.All(term => MatchesFreeText(entry, term));
        }

        private static bool MatchesCondition(
            LogEntry entry,
            QueryCondition condition,
            DateTimeOffset now)
        {
            return condition.Key.ToLowerInvariant() switch
            {
                "kind" => MatchesCode(entry.Kind.ToString(), condition.Value),
                "event" => MatchesCode(entry.EventName, condition.Value),
                "category" => MatchesCode(entry.DisplayCategory, condition.Value),
                "origin" => MatchesCode(entry.Origin, condition.Value),
                "outcome" => MatchesCode(entry.Outcome, condition.Value),
                "module" => MatchesCode(entry.Module, condition.Value),
                "level" => MatchesLevel(entry.Level, condition.Value),
                "after" => TryParseDuration(condition.Value, out var duration)
                    && entry.Timestamp >= now.Subtract(duration),
                "text" => MatchesFreeText(entry, condition.Value),
                _ => true,
            };
        }

        private static bool MatchesFreeText(LogEntry entry, string term)
        {
            if (Contains(entry.Message, term)
                || Contains(entry.Exception, term)
                || Contains(entry.EventName, term)
                || Contains(entry.DisplayCategory, term)
                || Contains(entry.Origin, term)
                || Contains(entry.Outcome, term)
                || Contains(entry.Module, term))
            {
                return true;
            }

            return entry.Properties.Any(property =>
                Contains(property.Name, term) || Contains(property.Value, term));
        }

        private static bool MatchesCode(string actual, string expected)
        {
            if (string.IsNullOrWhiteSpace(actual))
            {
                return false;
            }

            if (expected.EndsWith('*'))
            {
                return actual.StartsWith(expected[..^1], StringComparison.OrdinalIgnoreCase);
            }

            return string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase);
        }

        private static bool MatchesLevel(LogLevel actual, string expected)
        {
            var minimumMatch = expected.EndsWith('+');
            var levelText = minimumMatch ? expected[..^1] : expected;
            if (!Enum.TryParse<LogLevel>(levelText, true, out var level))
            {
                return false;
            }

            return minimumMatch ? actual >= level : actual == level;
        }

        private static bool Contains(string value, string term)
        {
            return !string.IsNullOrWhiteSpace(value)
                && value.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool IsSupportedKey(string key)
        {
            return key.Equals("kind", StringComparison.OrdinalIgnoreCase)
                || key.Equals("event", StringComparison.OrdinalIgnoreCase)
                || key.Equals("category", StringComparison.OrdinalIgnoreCase)
                || key.Equals("origin", StringComparison.OrdinalIgnoreCase)
                || key.Equals("outcome", StringComparison.OrdinalIgnoreCase)
                || key.Equals("module", StringComparison.OrdinalIgnoreCase)
                || key.Equals("level", StringComparison.OrdinalIgnoreCase)
                || key.Equals("after", StringComparison.OrdinalIgnoreCase)
                || key.Equals("text", StringComparison.OrdinalIgnoreCase);
        }

        private static bool TryParseDuration(string value, out TimeSpan duration)
        {
            duration = default;
            if (string.IsNullOrWhiteSpace(value) || value.Length < 2)
            {
                return false;
            }

            if (!double.TryParse(
                    value[..^1],
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out var amount)
                || amount < 0)
            {
                return false;
            }

            try
            {
                duration = char.ToLowerInvariant(value[^1]) switch
                {
                    's' => TimeSpan.FromSeconds(amount),
                    'm' => TimeSpan.FromMinutes(amount),
                    'h' => TimeSpan.FromHours(amount),
                    'd' => TimeSpan.FromDays(amount),
                    _ => default,
                };
            }
            catch (OverflowException)
            {
                return false;
            }

            return duration > TimeSpan.Zero;
        }

        private sealed class QueryCondition
        {
            public string Key { get; }

            public string Value { get; }

            public QueryCondition(string key, string value)
            {
                Key = key;
                Value = value;
            }
        }
    }
}

using Prometheus.Core.Models;

namespace Prometheus.Services.Client
{
    internal static class MatchGameModeResolver
    {
        public static void Apply(
            IEnumerable<Match> matches,
            IReadOnlyList<GameQueue> queues)
        {
            var queueNames = (queues ?? Array.Empty<GameQueue>())
                .Where(queue => queue is not null &&
                                !string.IsNullOrWhiteSpace(queue.DisplayName))
                .GroupBy(queue => queue.Id)
                .ToDictionary(group => group.Key, group => group.First().DisplayName);

            foreach (var match in matches ?? Array.Empty<Match>())
            {
                if (match is null)
                {
                    continue;
                }

                if (queueNames.TryGetValue(match.QueueId, out var queueName))
                {
                    match.DisplayGameMode = queueName;
                }
                else if (string.IsNullOrWhiteSpace(match.DisplayGameMode))
                {
                    match.DisplayGameMode = match.GameMode;
                }
            }
        }
    }
}

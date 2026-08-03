using Prometheus.Core.Models;

namespace Prometheus.Shared.Models
{
    public sealed class SummonerQuickPreview
    {
        public SummonerAccount Summoner { get; init; }

        public string ProfileIcon { get; init; }

        public Rank Solo { get; init; }

        public Rank Flex { get; init; }

        public int MatchCount { get; init; }

        public int Wins { get; init; }

        public int Losses { get; init; }

        public string WinRate { get; init; }

        public string Kda { get; init; }

        public IReadOnlyList<RecentMatchResult> Results { get; init; } =
            Array.Empty<RecentMatchResult>();
    }

    public sealed class RecentMatchResult
    {
        public int Index { get; init; }

        public bool IsWin { get; init; }
    }
}

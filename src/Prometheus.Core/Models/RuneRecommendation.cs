namespace Prometheus.Core.Models
{
    public enum RuneRecommendationKind
    {
        Popular,
        WinRate
    }

    public sealed class RuneRecommendationOption
    {
        public RuneRecommendationKind Kind { get; init; }

        public int PrimaryStyleId { get; init; }

        public int SubStyleId { get; init; }

        public IReadOnlyList<int> SelectedPerkIds { get; init; } = [];

        public long SampleCount { get; init; }

        public int PickRateBasisPoints { get; init; }

        public int WinRateBasisPoints { get; init; }
    }

    public sealed class RuneRecommendationSet
    {
        public int ChampionId { get; init; }

        public string Lane { get; init; } = string.Empty;

        public string Source { get; init; } = string.Empty;

        public string DataVersion { get; init; } = string.Empty;

        public DateTimeOffset? UpdatedAt { get; init; }

        public RuneRecommendationOption Popular { get; init; }

        public RuneRecommendationOption WinRate { get; init; }
    }

    public enum RunePageApplyStatus
    {
        Applied,
        ClientUnavailable,
        InvalidRecommendation,
        ConfirmationFailed
    }

    public sealed class RunePageApplyResult
    {
        public RunePageApplyStatus Status { get; init; }

        public long RunePageId { get; init; }

        public bool PageCreated { get; init; }

        public bool Succeeded => Status == RunePageApplyStatus.Applied;
    }
}

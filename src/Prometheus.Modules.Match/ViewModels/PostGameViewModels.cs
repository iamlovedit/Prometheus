namespace Prometheus.Modules.Match.ViewModels
{
    public enum PostGameMetric
    {
        ChampionDamage,
        GoldEarned,
        DamageTaken
    }

    public sealed class PostGamePlayerRowViewModel
    {
        public string ChampionIcon { get; init; } = string.Empty;

        public string ChampionFallbackText { get; init; } = string.Empty;

        public string DisplayName { get; init; } = string.Empty;

        public string KdaText { get; init; } = string.Empty;

        public string GoldText { get; init; } = string.Empty;

        public string CreepScoreText { get; init; } = string.Empty;

        public string ChampionDamageText { get; init; } = string.Empty;

        public string DamageTakenText { get; init; } = string.Empty;

        public string VisionScoreText { get; init; } = string.Empty;

        public string TeamShareText { get; init; } = string.Empty;

        public bool IsMyTeam { get; init; }

        public bool IsLocalPlayer { get; init; }
    }
}

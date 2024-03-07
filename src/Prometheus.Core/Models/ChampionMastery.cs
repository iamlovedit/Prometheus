namespace Prometheus.Core.Models
{
    public class ChampionMastery
    {
        public int ChampionId { get; set; }

        public int ChampionLevel { get; set; }

        public int ChampionPoints { get; set; }

        public int ChampionPointsSinceLastLevel { get; set; }

        public int ChampionPointsUntilNextLevel { get; set; }

        public bool ChestGranted { get; set; }

        public string FormattedChampionPoints { get; set; }

        public string FormattedMasteryGoal { get; set; }

        public string HighestGrade { get; set; }

        public long LastPlayTime { get; set; }

        public int TokensEarned { get; set; }

        public string ChampionIcon { get; set; }
    }
}

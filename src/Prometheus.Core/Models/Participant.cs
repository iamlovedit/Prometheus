namespace Prometheus.Core.Models
{
    public class Participant
    {
        public int ChampionId { get; set; }

        public string ChampionIcon { get; set; }

        public int ParticipantId { get; set; }

        public int Spell1Id { get; set; }

        public int Spell2Id { get; set; }

        public MatchStats Stats { get; set; }

        public string Spell1Icon { get; set; }

        public string Spell2Icon { get; set; }

        public int TeamId { get; set; }
    }
}

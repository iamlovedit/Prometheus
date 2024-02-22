namespace Prometheus.Modules.Summoner.Models
{
    public class SummonerAccount
    {
        public long AccountId { get; set; }

        public string DisplayName { get; set; }

        public string GameName { get; set; }

        public string InternalName { get; set; }

        public string NameChangeFlag { get; set; }

        public int PercentCompleteForNextLevel { get; set; }

        public string Privacy { get; set; }

        public int ProfileIconId { get; set; }

        public string Puuid { get; set; }

        public RerollPoints RerollPoints { get; set; }

        public long SummonerId { get; set; }

        public int SummonerLevel { get; set; }

        public string TagLine { get; set; }

        public string Unnamed { get; set; }

        public int XpSinceLastLevel { get; set; }

        public int XpUntilNextLevel { get; set; }
    }
}

namespace Prometheus.Shared.Models
{
    public class Player
    {
        public string ChampionIcon { get; set; }

        public string Puuid { get; set; }

        public bool Win { get; set; }

        public uint Id { get; set; }

        public string SummonerName { get; set; }

        private string _name;
        public string Name
        {
            get
            {
                if (string.IsNullOrEmpty(_name))
                {
                    return SummonerName;
                }
                return _name;
            }
            set
            {
                _name = value;
            }
        }

        public uint PerkId { get; set; }

        public string PerkIcon { get; set; }

        public string Spell1Icon { get; set; }

        public string Spell2Icon { get; set; }

        private string _kda;
        public string KDA
        {
            get
            {
                if (string.IsNullOrEmpty(_kda))
                {
                    return $"{Kills}/{Deaths}/{Assists}";
                }
                return _kda;
            }
        }

        public uint Assists { get; set; }

        public byte ChampLevel { get; set; }

        public uint Deaths { get; set; }

        public uint Kills { get; set; }

        public uint GoldEarned { get; set; }

        public string Item0Icon { get; set; }

        public string Item1Icon { get; set; }

        public string Item2Icon { get; set; }

        public string Item3Icon { get; set; }

        public string Item4Icon { get; set; }

        public string Item5Icon { get; set; }

        public string Item6Icon { get; set; }

        public ulong TotalDamage { get; set; }
    }
}

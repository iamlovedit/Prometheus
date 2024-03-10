namespace Prometheus.Shared.Models
{
    public class Player
    {
        public string ChampionIcon { get; set; }

        public bool Win { get; set; }

        public uint Id { get; set; }

        public string Name { get; set; }

        public uint PerkId { get; set; }

        public uint Spell1Id { get; set; }

        public uint Spell2Id { get; set; }

        public uint Assists { get; set; }

        public byte ChampLevel { get; set; }

        public uint Deaths { get; set; }

        public uint Kills { get; set; }

        public uint GoldEarned { get; set; }

        public uint Item0 { get; set; }

        public uint Item1 { get; set; }

        public uint Item2 { get; set; }

        public uint Item3 { get; set; }

        public uint Item4 { get; set; }

        public uint Item5 { get; set; }

        public uint Item6 { get; set; }

        public ulong TotalDamage { get; set; }
    }
}

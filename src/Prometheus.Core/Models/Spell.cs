using System.Collections.Generic;

namespace Prometheus.Core.Models
{
    public class Spell
    {
        public long Id { get; set; }

        public string Name { get; set; }

        public string Description { get; set; }

        public int SummonerLevel { get; set; }

        public int Cooldown { get; set; }

        public List<string> GameModes { get; set; }

        public string IconPath { get; set; }
    }
}


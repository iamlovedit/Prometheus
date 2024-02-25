using System.Collections.Generic;

namespace Prometheus.Core.Models
{
    public class ChampionSummary
    {
        public int Id { get; set; }

        public string Name { get; set; }

        public string Alias { get; set; }

        public string SquarePortraitPath { get; set; }

        public List<string> Roles { get; set; }
    }
}


using System.Collections.Generic;

namespace Prometheus.Core.Models
{
    public class Team
    {
        public List<BanChampion> Bans { get; set; }

        public int BaronKills { get; set; }

        public int DominionVictoryScore { get; set; }

        public int DragonKills { get; set; }

        public bool FirstBaron { get; set; }

        public bool FirstBlood { get; set; }

        public bool FirstDargon { get; set; }

        public bool FirstInhibitor { get; set; }

        public bool FirstTower { get; set; }

        public int HordeKills { get; set; }

        public int InhibitorKills { get; set; }

        public int RiftHeraldKills { get; set; }

        public int TeamId { get; set; }

        public int TowerKills { get; set; }

        public int VilemawKills { get; set; }

        public string Win { get; set; }
    }
}

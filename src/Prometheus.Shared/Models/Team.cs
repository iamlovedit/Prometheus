using System.Collections.Generic;
using System.Linq;

namespace Prometheus.Shared.Models
{
    public class Team
    {
        public bool Win
        {
            get
            {
                return Players?.FirstOrDefault()?.Win ?? false;
            }
        }

        private string _kda;
        public string KDA
        {
            get
            {
                if (string.IsNullOrEmpty(_kda))
                {
                    _kda = $"{Players.Sum(p => p.Kills)}/{Players.Sum(p => p.Deaths)}/{Players.Sum(p => p.Assists)}";
                }
                return _kda;
            }

        }

        public uint Gold => (uint)Players.Sum(p => p.GoldEarned);

        public List<Player> Players { get; set; }
    }
}

using System.Collections.Generic;

namespace Prometheus.Core.Models
{
    public class Perk
    {
        public int Id { get; set; }

        public string Name { get; set; }

        public string MajorChangePatchVersion { get; set; }

        public string Tooltip { get; set; }

        public string ShortDesc { get; set; }

        public string LongDesc { get; set; }

        public string RecommendationDescriptor { get; set; }

        public string IconPath { get; set; }

        public List<string> EndOfGameStatDescs { get; set; }
    }
}


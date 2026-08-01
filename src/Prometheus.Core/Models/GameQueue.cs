using Newtonsoft.Json;

namespace Prometheus.Core.Models
{
    public sealed class GameQueue
    {
        public int Id { get; set; }

        public string Name { get; set; } = string.Empty;

        public string ShortName { get; set; } = string.Empty;

        public string Description { get; set; } = string.Empty;

        public string GameMode { get; set; } = string.Empty;

        [JsonIgnore]
        public string DisplayName
        {
            get
            {
                if (!string.IsNullOrWhiteSpace(ShortName))
                {
                    return ShortName;
                }

                if (!string.IsNullOrWhiteSpace(Name))
                {
                    return Name;
                }

                if (!string.IsNullOrWhiteSpace(Description))
                {
                    return Description;
                }

                return GameMode;
            }
        }
    }
}

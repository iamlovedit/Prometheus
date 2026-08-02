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

        public string QueueAvailability { get; set; } = string.Empty;

        public bool IsEnabled { get; set; }

        public bool IsVisible { get; set; }

        public int MaximumParticipantListSize { get; set; }

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

    public static class GameQueueIds
    {
        public const int RankedSoloDuo = 420;

        public const int RankedFlex = 440;

        public const int Aram = 450;

        public const int HextechAram = 2400;
    }
}

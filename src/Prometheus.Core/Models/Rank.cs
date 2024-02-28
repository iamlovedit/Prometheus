using Newtonsoft.Json.Converters;
using Newtonsoft.Json;
using System;

namespace Prometheus.Core.Models
{
    public class Rank
    {
        public string Division { get; set; }

        public int LeaguePoints { get; set; }

        public int Losses { get; set; }

        public int Wins { get; set; }

        public Tier Tier { get; set; }

        public bool IsProvisional { get; set; }

        public string PreviousSeasonEndDivision { get; set; }

        public Tier PreviousSeasonEndTier { get; set; }

        public QueueType QueueType { get; set; }

        public double RatedRating { get; set; }

    }

    public enum QueueType
    {
        RANKED_SOLO_5x5,
        RANKED_FLEX_SR,
        RANKED_TFT,
        RANKED_TFT_TURBO,
    }

    [JsonConverter(typeof(TierEnumConverter))]
    public enum Tier
    {
        UNRANKED,
        IRON,
        BRONZE,
        SILVER,
        GOLD,
        PLATINUM,
        EMERALD,
        DIAMOND,
        MASTER,
        GRANDMASTER,
        CHALLENGER
    }

    public class TierEnumConverter : StringEnumConverter
    {
        public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
        {
            if (string.IsNullOrEmpty(reader.Value.ToString()))
            {
                return Tier.UNRANKED;
            }
            return base.ReadJson(reader, objectType, existingValue, serializer);
        }
    }
}


using Newtonsoft.Json.Converters;
using Newtonsoft.Json;
using System;
using System.Reflection;

namespace Prometheus.Core.Models
{
    public class Rank
    {
        public string Division { get; set; }

        public string HighestDivision { get; set; }

        public string PreviousSeasonEndDivision { get; set; }

        public int LeaguePoints { get; set; }

        public int Losses { get; set; }

        public int Wins { get; set; }

        public Tier Tier { get; set; }

        public Tier HighestTier { get; set; }

        public Tier PreviousSeasonEndTier { get; set; }

        public bool IsProvisional { get; set; }

        public QueueType QueueType { get; set; }

        public double RatedRating { get; set; }

        public int Count => Wins + Losses;

        public string WinRate
        {
            get
            {
                if (Count == 0)
                {
                    return "0";
                }
                return $"{Math.Round(Wins * 100d / Count, 2, MidpointRounding.AwayFromZero)} %";
            }
        }

        public string DisplayHighestTier
        {
            get
            {
                var attribute = DisplayKeyAttribute.GetDisplayKey(HighestTier);
                if (attribute != null)
                {
                    return $"{attribute.GetDisplayValue()} {HighestDivision}";
                }
                return string.Empty;
            }
        }

        public string DisplayPreviosHighestTier
        {
            get
            {
                var attribute = DisplayKeyAttribute.GetDisplayKey(PreviousSeasonEndTier);
                if (attribute != null)
                {
                    return $"{attribute.GetDisplayValue()} {PreviousSeasonEndDivision}";
                }
                return string.Empty;
            }
        }

        public string DisplayTier
        {
            get
            {
                var attribute = DisplayKeyAttribute.GetDisplayKey(Tier);
                if (attribute != null)
                {
                    return $"{attribute.GetDisplayValue()} {Division}";
                }
                return string.Empty;
            }
        }

        public string DisplayQueueType
        {
            get
            {
                var attribute = DisplayKeyAttribute.GetDisplayKey(QueueType);
                return attribute?.GetDisplayValue();
            }
        }
    }

    public enum MapSide
    {
        Blue,
        Red,
    }

    [JsonConverter(typeof(StringEnumConverter))]
    public enum QueueType
    {
        [DisplayKey("Match.Rank.Solo")]
        RANKED_SOLO_5x5,
        [DisplayKey("Match.Rank.Flex")]
        RANKED_FLEX_SR,
        [DisplayKey("Match.Rank.TFT")]
        RANKED_TFT,
        [DisplayKey("")]
        RANKED_TFT_TURBO,
    }

    [JsonConverter(typeof(DivisionEnumConverter))]
    public enum Division
    {
        NA,
        I,
        II,
        III,
        IV
    }

    [JsonConverter(typeof(TierEnumConverter))]
    public enum Tier
    {
        [DisplayKey("Career.Rank.Tier.Unranked")]
        UNRANKED,
        [DisplayKey("Career.Rank.Tier.Iron")]
        IRON,
        [DisplayKey("Career.Rank.Tier.Bronze")]
        BRONZE,
        [DisplayKey("Career.Rank.Tier.Silver")]
        SILVER,
        [DisplayKey("Career.Rank.Tier.Gold")]
        GOLD,
        [DisplayKey("Career.Rank.Tier.Platinum")]
        PLATINUM,
        [DisplayKey("Career.Rank.Tier.Emerald")]
        EMERALD,
        [DisplayKey("Career.Rank.Tier.Diamond")]
        DIAMOND,
        [DisplayKey("Career.Rank.Tier.Master")]
        MASTER,
        [DisplayKey("Career.Rank.Tier.Grandmaster")]
        GRANDMASTER,
        [DisplayKey("Career.Rank.Tier.Challenger")]
        CHALLENGER
    }

    public class DivisionEnumConverter : StringEnumConverter
    {
        public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
        {
            if (string.IsNullOrEmpty(reader.Value.ToString()))
            {
                return Division.NA;
            }
            return base.ReadJson(reader, objectType, existingValue, serializer);
        }
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


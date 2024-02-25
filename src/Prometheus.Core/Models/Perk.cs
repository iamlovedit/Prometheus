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

    public class Skin
    {
        public int Id { get; set; }

        public bool IsBase { get; set; }

        public string Name { get; set; }

        public string SplashPath { get; set; }

        public string UncenteredSplashPath { get; set; }

        public string TilePath { get; set; }

        public string LoadScreenPath { get; set; }

        public string SkinType { get; set; }

        public string Rarity { get; set; }

        public bool IsLegacy { get; set; }

        public string SplashVideoPath { get; set; }

        public string CollectionSplashVideoPath { get; set; }

        public string FeaturesText { get; set; }

        public string ChromaPath { get; set; }

        public List<Emblems> Emblems { get; set; }

        public int RegionRarityId { get; set; }

        public string RarityGemPath { get; set; }

        public List<SkinLines> SkinLines { get; set; }

        public string SkinAugments { get; set; }

        public string Description { get; set; }
    }

    public class SkinLines
    {
        public int Id { get; set; }
    }

    public class Emblems
    {
        public string Name { get; set; }

        public EmblemPath EmblemPath { get; set; }

    }

    public class EmblemPath
    {
        public string Large { get; set; }

        public string Small { get; set; }
    }
}


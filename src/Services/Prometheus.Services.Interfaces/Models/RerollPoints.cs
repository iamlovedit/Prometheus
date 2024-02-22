namespace Prometheus.Services.Interfaces.Models
{
    public class RerollPoints
    {
        public int CurrentPoints { get; set; }

        public int MaxRolls { get; set; }

        public int NumberOfRolls { get; set; }

        public int PointsCostToRoll { get; set; }

        public int PointsToReroll { get; set; }
    }
}

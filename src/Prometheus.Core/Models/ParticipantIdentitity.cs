namespace Prometheus.Core.Models
{
    public class ParticipantIdentitity
    {
        public int ParticipantId { get; set; }

        public SummonerAccount Player { get; set; }
    }
}

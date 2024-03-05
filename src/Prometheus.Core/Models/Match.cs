using System;
using System.Collections.Generic;

namespace Prometheus.Core.Models
{
    public class Match
    {
        public long GameId { get; set; }

        public long GameCreation { get; set; }

        public DateTime GameCreationDate { get; set; }

        public string GameMode { get; set; }

        public string DisplayGameMode { get; set; }

        public int MapId { get; set; }

        public List<Participant> Participants { get; set; }
    }

    public class MatchDetail : Match
    {
        public List<ParticipantIdentitity> ParticipantIdentities { get; set; }

        public List<Team> Teams { get; set; }
    }
}

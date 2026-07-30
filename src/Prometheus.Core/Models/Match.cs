using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;

namespace Prometheus.Core.Models
{
    public class MatchHistoryResponse
    {
        public MatchHistoryPage Games { get; set; }
    }

    public class MatchHistoryPage
    {
        public List<Match> Games { get; set; } = [];
    }

    /// <summary>
    /// Distinguishes a successful empty match history from an unavailable or
    /// malformed LCU response for live-match enrichment.
    /// </summary>
    public class MatchHistoryQueryResult
    {
        public bool Succeeded { get; set; }

        public IReadOnlyList<Match> Matches { get; set; } = Array.Empty<Match>();

        public string Error { get; set; } = string.Empty;
    }

    public class Match
    {
        public long GameId { get; set; }

        public long GameCreation { get; set; }

        private DateTime? _creationDate;
        public DateTime? CreationDate
        {
            get
            {
                if (!_creationDate.HasValue)
                {
                    var date = DateTimeOffset.FromUnixTimeMilliseconds(GameCreation);
                    _creationDate = TimeZoneInfo.ConvertTime(date, TimeZoneInfo.Local).DateTime;
                }
                return _creationDate;
            }
        }

        //public DateTime GameCreationDate { get; set; }

        public string GameMode { get; set; }

        public string DisplayGameMode { get; set; }

        public int MapId { get; set; }

        public List<Participant> Participants { get; set; }

    }

    public class MatchDetail : Match
    {
        public List<ParticipantIdentitity> ParticipantIdentities { get; set; }

        public long GameDuration { get; set; }

        private int? _duration;
        public int? Duration
        {
            get
            {
                if (!_duration.HasValue)
                {
                    _duration = (CreationDate.Value.AddSeconds(GameDuration) - CreationDate.Value).Duration().Minutes;
                }
                return _duration;
            }
        }

        public List<Team> Teams { get; set; }

        private string _kills;
        public string Kills
        {
            get
            {
                if (string.IsNullOrEmpty(_kills))
                {
                    var pGroups = Participants.GroupBy(p => p.TeamId).OrderBy(g => g.Key).ToArray();
                    var team1Kills = pGroups[0]?.Sum(p => p.Stats.Kills);
                    var team2Kills = pGroups[1]?.Sum(p => p.Stats.Kills);
                    _kills = $"{team1Kills}/{team2Kills}";
                }
                return _kills;
            }
        }
    }
}

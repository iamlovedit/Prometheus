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

    public class Participant
    {
        public int ChampionId { get; set; }

        public string ChampionIcon { get; set; }

        public int ParticipantId { get; set; }

        public int Spell1Id { get; set; }

        public int Spell2Id { get; set; }

        public MatchStats Stats { get; set; }

        public string Spell1Icon { get; set; }

        public string Spell2Icon { get; set; }
    }

    public class MatchStats
    {
        public string PerkIcon { get; set; }

        public string KDA => $"{Kills}/{Deaths}/{Assists}";

        public int Assists { get; set; }

        public bool Win { get; set; }

        public bool CausedEarlySurrender { get; set; }

        public int ChampLevel { get; set; }

        public int Deaths { get; set; }

        public int Kills { get; set; }

        public int Perk0 { get; set; }

        public int Item0 { get; set; }

        public int Item1 { get; set; }

        public int Item2 { get; set; }

        public int Item3 { get; set; }

        public int Item4 { get; set; }

        public int Item5 { get; set; }

        public int Item6 { get; set; }

        public string Item0Icon { get; set; }

        public string Item1Icon { get; set; }

        public string Item2Icon { get; set; }

        public string Item3Icon { get; set; }

        public string Item4Icon { get; set; }

        public string Item5Icon { get; set; }

        public string Item6Icon { get; set; }

        public int GoldEarned { get; set; }

        public int TotalDamageDealtToChampions { get; set; }
    }
}

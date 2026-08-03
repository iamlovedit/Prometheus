
namespace Prometheus.Core.Models
{
    /// <summary>
    /// Connection state exposed by the live match coordinator.
    /// </summary>
    public enum ConnectionState
    {
        Disconnected,
        Connecting,
        Connected,
        Reconnecting,
        Stopping,
        Error
    }

    /// <summary>
    /// The gameflow phases reported by the League client.  Unknown is used for
    /// new phases introduced by the client so consumers can continue to work.
    /// </summary>
    public enum GameflowPhase
    {
        Unknown,
        None,
        Lobby,
        Matchmaking,
        ReadyCheck,
        ChampSelect,
        GameStart,
        InProgress,
        WaitingForStats,
        PreEndOfGame,
        EndOfGame,
        Reconnect,
        TerminatedInError
    }

    public enum DataQuality
    {
        Unknown,
        Partial,
        Complete,
        Stale,
        Error
    }

    /// <summary>
    /// Immutable-at-publication (the coordinator replaces the instance on
    /// every change) view of all LCU resources needed by the match UI.
    /// </summary>
    public class LiveMatchSnapshot
    {
        /// <summary>
        /// Monotonically increasing publication version. Consumers can use it
        /// to discard event callbacks that arrive after a newer snapshot.
        /// </summary>
        public long Version { get; set; }

        public ConnectionState ConnectionState { get; set; } = ConnectionState.Disconnected;

        public GameflowPhase GameflowPhase { get; set; } = GameflowPhase.Unknown;

        /// <summary>The exact phase string supplied by the client.</summary>
        public string RawPhase { get; set; } = string.Empty;

        public GameflowSessionSnapshot GameflowSession { get; set; }

        public LobbySnapshot Lobby { get; set; }

        public MatchmakingSnapshot Matchmaking { get; set; }

        public ReadyCheckSnapshot ReadyCheck { get; set; }

        public ChampionSelectSnapshot ChampionSelect { get; set; }

        public PostGameSnapshot PostGame { get; set; }

        /// <summary>
        /// Service-owned, progressively enriched roster for the live match.
        /// </summary>
        public LiveMatchRosterSnapshot Roster { get; set; }

        public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

        // Friendly aliases used by callers that prefer a more explicit name.
        public DateTimeOffset LastUpdatedAt
        {
            get => UpdatedAt;
            set => UpdatedAt = value;
        }

        public DateTimeOffset Timestamp
        {
            get => UpdatedAt;
            set => UpdatedAt = value;
        }

        public DataQuality DataQuality { get; set; } = DataQuality.Unknown;

        public string Error { get; set; } = string.Empty;

        public string LastError
        {
            get => Error;
            set => Error = value;
        }

        public IReadOnlyList<string> Errors { get; set; } = Array.Empty<string>();

        public static LiveMatchSnapshot Empty => new();
    }

    public sealed class LiveMatchSnapshotChangedEventArgs : EventArgs
    {
        public LiveMatchSnapshotChangedEventArgs(LiveMatchSnapshot snapshot)
        {
            Snapshot = snapshot ?? throw new ArgumentNullException(nameof(snapshot));
        }

        public LiveMatchSnapshot Snapshot { get; }
    }

    public enum LiveMatchPlayerDataState
    {
        Placeholder,
        Hidden,
        Loading,
        Loaded,
        Unavailable,
        Error
    }

    /// <summary>
    /// A five-versus-five roster assembled and enriched exclusively by
    /// <c>MatchService</c>. The two lists are replaced as a unit on every
    /// publication.
    /// </summary>
    public class LiveMatchRosterSnapshot
    {
        public long GameId { get; set; }

        public GameflowPhase SourcePhase { get; set; } = GameflowPhase.Unknown;

        public string Signature { get; set; } = string.Empty;

        public bool IsResolving { get; set; }

        public IReadOnlyList<LiveMatchPlayerSnapshot> MyTeam { get; set; } =
            Array.Empty<LiveMatchPlayerSnapshot>();

        public IReadOnlyList<LiveMatchPlayerSnapshot> TheirTeam { get; set; } =
            Array.Empty<LiveMatchPlayerSnapshot>();
    }

    public class LiveMatchPlayerSnapshot
    {
        public int Slot { get; set; }

        public long CellId { get; set; }

        public int ChampionId { get; set; }

        public int Spell1Id { get; set; }

        public int Spell2Id { get; set; }

        public string ChampionIcon { get; set; } = string.Empty;

        public string Spell1Icon { get; set; } = string.Empty;

        public string Spell2Icon { get; set; } = string.Empty;

        public string Puuid { get; set; } = string.Empty;

        public string Position { get; set; } = string.Empty;

        public string DisplayName { get; set; } = string.Empty;

        public bool IsLocalPlayer { get; set; }

        public bool IsHidden { get; set; }

        public bool IsPlaceholder { get; set; }

        public LiveMatchPlayerDataState DataState { get; set; } =
            LiveMatchPlayerDataState.Placeholder;

        public SummonerAccount Summoner { get; set; }

        public Rank SoloRank { get; set; }

        public int RecentWins { get; set; }

        public int RecentLosses { get; set; }

        public int RecentMatchCount { get; set; }

        public double AverageKda { get; set; }

        public IReadOnlyList<bool> RecentResults { get; set; } = Array.Empty<bool>();

        public IReadOnlyList<LiveMatchRecentMatchSnapshot> RecentMatches { get; set; } =
            Array.Empty<LiveMatchRecentMatchSnapshot>();

        public string Error { get; set; } = string.Empty;
    }

    public class LiveMatchRecentMatchSnapshot
    {
        public long GameId { get; set; }

        public long GameCreation { get; set; }

        public int QueueId { get; set; }

        public string GameMode { get; set; } = string.Empty;

        public int ChampionId { get; set; }

        public string ChampionIcon { get; set; } = string.Empty;

        public bool IsWin { get; set; }

        public int Kills { get; set; }

        public int Deaths { get; set; }

        public int Assists { get; set; }
    }

    // The following DTOs deliberately contain only the small, stable subset
    // consumed by the UI, including the identity and visibility fields needed
    // to progressively enrich live-match player details.

    public class GameflowSessionSnapshot
    {
        public string Phase { get; set; } = string.Empty;

        public GameflowGameData GameData { get; set; }

        public GameflowClientState GameClient { get; set; }

        public GameflowMap Map { get; set; }
    }

    public class GameflowGameData
    {
        public long GameId { get; set; }

        public string GameMode { get; set; } = string.Empty;

        public string GameType { get; set; } = string.Empty;

        public int MapId { get; set; }

        public int QueueId { get; set; }

        public List<GameflowPlayerSelection> PlayerChampionSelections { get; set; } = [];

        public List<GameflowTeamMember> TeamOne { get; set; } = [];

        public List<GameflowTeamMember> TeamTwo { get; set; } = [];
    }

    public class GameflowClientState
    {
        public bool Running { get; set; }

        public bool ConnectedToServer { get; set; }

        public long Timestamp { get; set; }
    }

    public class GameflowMap
    {
        public int Id { get; set; }

        public string Name { get; set; } = string.Empty;
    }

    public class GameflowPlayerSelection
    {
        public long CellId { get; set; }

        public int ChampionId { get; set; }

        public string Puuid { get; set; } = string.Empty;

        public int SelectedSkinIndex { get; set; }

        public int Spell1Id { get; set; }

        public int Spell2Id { get; set; }
    }

    public class GameflowTeamMember
    {
        public long CellId { get; set; }

        public int ChampionId { get; set; }

        public string AssignedPosition { get; set; } = string.Empty;

        public string SelectedPosition { get; set; } = string.Empty;

        public string Puuid { get; set; } = string.Empty;

        public long SummonerId { get; set; }

        public string SummonerName { get; set; } = string.Empty;

        public int ProfileIconId { get; set; }

        public int Spell1Id { get; set; }

        public int Spell2Id { get; set; }

        public int TeamId { get; set; }

        public int TeamParticipantId { get; set; }
    }

    public class LobbySnapshot
    {
        public string PartyId { get; set; } = string.Empty;

        public string PartyType { get; set; } = string.Empty;

        public LobbyMemberSnapshot LocalMember { get; set; }

        public List<LobbyMemberSnapshot> Members { get; set; } = [];

        public LobbyGameConfiguration GameConfig { get; set; }

        public List<LobbyInvitationSnapshot> Invitations { get; set; } = [];

        public List<LobbyRestrictionSnapshot> Restrictions { get; set; } = [];

        public LobbySearchPreferences SearchPreferences { get; set; }
    }

    public class LobbyMemberSnapshot
    {
        public long SummonerId { get; set; }

        public string SummonerName { get; set; } = string.Empty;

        public int SummonerIconId { get; set; }

        public bool IsLeader { get; set; }

        public bool IsReady { get; set; }

        public string FirstPositionPreference { get; set; } = string.Empty;

        public string SecondPositionPreference { get; set; } = string.Empty;
    }

    public class LobbyGameConfiguration
    {
        public int QueueId { get; set; }

        public string GameMode { get; set; } = string.Empty;

        public int MapId { get; set; }

        public int TeamSize { get; set; }

        public string SpectatorPolicy { get; set; } = string.Empty;
    }

    public class LobbyInvitationSnapshot
    {
        public string InvitationId { get; set; } = string.Empty;

        public string State { get; set; } = string.Empty;
    }

    public class LobbyRestrictionSnapshot
    {
        public string Reason { get; set; } = string.Empty;

        public string Type { get; set; } = string.Empty;
    }

    public class LobbySearchPreferences
    {
        public string[] PartyType { get; set; } = Array.Empty<string>();

        public string[] Positions { get; set; } = Array.Empty<string>();
    }

    public class MatchmakingSnapshot
    {
        public string SearchState { get; set; } = string.Empty;

        public bool IsCurrentlyInQueue { get; set; }

        public double EstimatedQueueTime { get; set; }

        public double TimeInQueue { get; set; }

        public string LobbyId { get; set; } = string.Empty;

        public MatchmakingQueue Queue { get; set; }

        public MatchmakingDodgeData DodgeData { get; set; }

        public MatchmakingLowPriorityData LowPriorityData { get; set; }
    }

    public class MatchmakingQueue
    {
        public int Id { get; set; }

        public string Name { get; set; } = string.Empty;
    }

    public class MatchmakingDodgeData
    {
        public string State { get; set; } = string.Empty;

        public int RemainingDodges { get; set; }
    }

    public class MatchmakingLowPriorityData
    {
        public bool IsLowPriority { get; set; }

        public double PenaltyTime { get; set; }
    }

    public class ReadyCheckSnapshot
    {
        public string State { get; set; } = string.Empty;

        public string PlayerResponse { get; set; } = string.Empty;

        public string DeclineReason { get; set; } = string.Empty;

        public double Timer { get; set; }

        public double TotalTimeInPhase { get; set; }

        public double AdjustedTimeLeftInPhase { get; set; }

        public List<ReadyCheckMemberSnapshot> Members { get; set; } = [];
    }

    public class ReadyCheckMemberSnapshot
    {
        public long PlayerSlot { get; set; }

        public string PlayerResponse { get; set; } = string.Empty;

        public bool IsMyTeam { get; set; }
    }

    public class ChampionSelectSnapshot
    {
        private List<List<ChampionSelectActionSnapshot>> _actions = [];

        public long GameId { get; set; }

        public long LocalPlayerCellId { get; set; }

        /// <summary>
        /// LCU sends actions as an array of arrays (one array per phase/round).
        /// The setter normalises null payloads to an empty two-dimensional list.
        /// </summary>
        public List<List<ChampionSelectActionSnapshot>> Actions
        {
            get => _actions;
            set => _actions = value ?? [];
        }

        public ChampionSelectBansSnapshot Bans { get; set; }

        public List<ChampionSelectTeamMemberSnapshot> MyTeam { get; set; } = [];

        public List<ChampionSelectTeamMemberSnapshot> TheirTeam { get; set; } = [];

        public bool BenchEnabled { get; set; }

        public List<ChampionSelectBenchChampionSnapshot> BenchChampions { get; set; } = [];

        public ChampionSelectTimerSnapshot Timer { get; set; }

        public string Phase { get; set; } = string.Empty;
    }

    public class ChampionSelectActionSnapshot
    {
        public int Id { get; set; }

        public long ActorCellId { get; set; }

        public int ChampionId { get; set; }

        public bool Completed { get; set; }

        public bool IsAllyAction { get; set; }

        public bool IsInProgress { get; set; }

        public string Type { get; set; } = string.Empty;

        public int PickTurn { get; set; }

        public long Duration { get; set; }
    }

    public class ChampionSelectBansSnapshot
    {
        public List<int> MyTeamBans { get; set; } = [];

        public List<int> TheirTeamBans { get; set; } = [];

        public int NumBans { get; set; }
    }

    public class ChampionSelectTeamMemberSnapshot
    {
        public long CellId { get; set; }

        public int ChampionId { get; set; }

        public int ChampionPickIntent { get; set; }

        public int SelectedSkinId { get; set; }

        public string AssignedPosition { get; set; } = string.Empty;

        public string GameName { get; set; } = string.Empty;

        public string NameVisibilityType { get; set; } = string.Empty;

        public string ObfuscatedPuuid { get; set; } = string.Empty;

        public long ObfuscatedSummonerId { get; set; }

        public string Puuid { get; set; } = string.Empty;

        public int Spell1Id { get; set; }

        public int Spell2Id { get; set; }

        public long SummonerId { get; set; }

        public string TagLine { get; set; } = string.Empty;

        public int Team { get; set; }

        public int WardSkinId { get; set; }

        public int PickTurn { get; set; }
    }

    public class ChampionSelectBenchChampionSnapshot
    {
        public int ChampionId { get; set; }

        public bool IsPriority { get; set; }
    }

    public class ChampionSelectTimerSnapshot
    {
        public string Phase { get; set; } = string.Empty;

        public long TotalTimeInPhase { get; set; }

        public long AdjustedTimeLeftInPhase { get; set; }
    }

    public class PostGameSnapshot
    {
        public long GameId { get; set; }

        public long GameLength { get; set; }

        public string GameMode { get; set; } = string.Empty;

        public int MapId { get; set; }

        public int QueueId { get; set; }

        public PostGamePlayerSnapshot LocalPlayer { get; set; }

        public List<PostGameTeamSnapshot> Teams { get; set; } = [];
    }

    public class PostGamePlayerSnapshot
    {
        public int ChampionId { get; set; }

        public int Kills { get; set; }

        public int Deaths { get; set; }

        public int Assists { get; set; }

        public bool Won { get; set; }
    }

    public class PostGameTeamSnapshot
    {
        public string Team { get; set; } = string.Empty;

        public bool Won { get; set; }
    }
}

using Prism.Mvvm;
using Prometheus.Core.Models;
using System.Collections.ObjectModel;

namespace Prometheus.Modules.Match.ViewModels
{
    public sealed class RecentMatchResultViewModel : BindableBase
    {
        private bool _isWin;

        public bool IsWin
        {
            get => _isWin;
            set => SetProperty(ref _isWin, value);
        }

        private string _resultTooltip = string.Empty;

        /// <summary>
        /// Hover text for a single segment of the 20-slot result strip.
        /// </summary>
        public string ResultTooltip
        {
            get => _resultTooltip;
            set => SetProperty(ref _resultTooltip, value);
        }
    }

    public sealed class LiveMatchRecentMatchViewModel
    {
        public long GameId { get; init; }

        public string IndexText { get; init; } = string.Empty;

        public string ResultText { get; init; } = string.Empty;

        public string GameModeText { get; init; } = string.Empty;

        public string ChampionIcon { get; init; } = string.Empty;

        public string ChampionFallbackText { get; init; } = string.Empty;

        public int Kills { get; init; }

        public int Deaths { get; init; }

        public int Assists { get; init; }

        public bool IsWin { get; init; }

        public string AutomationText { get; init; } = string.Empty;
    }

    /// <summary>
    /// Bindable row projected from one <see cref="LiveMatchPlayerSnapshot"/>.
    /// Instances are replaced when a newer live-match snapshot is published.
    /// </summary>
    public sealed class LiveMatchPlayerViewModel : BindableBase
    {
        private string _championIcon;
        private string _spell1Icon;
        private string _spell2Icon;
        private string _displayName;
        private string _positionText;
        private string _rankText;
        private string _recentRecordText;
        private string _kdaText;
        private string _statusText;
        private bool _isLocalPlayer;
        private bool _isHidden;
        private bool _isPlaceholder;
        private bool _isLoading;
        private bool _hasError;
        private bool _hasPerformanceData;
        private bool _hasRecentMatchDetails;
        private bool _canOpenProfile;
        private bool _isMyTeam;
        private bool _isSelected;
        private int _slot;
        private LiveMatchPlayerDataState _dataState;
        private string _puuid = string.Empty;
        private SummonerAccount _summoner;

        public LiveMatchPlayerViewModel()
        {
            RecentResults = [];
            RecentMatches = [];
        }

        public string ChampionIcon
        {
            get => _championIcon;
            set => SetProperty(ref _championIcon, value);
        }

        public string Spell1Icon
        {
            get => _spell1Icon;
            set => SetProperty(ref _spell1Icon, value);
        }

        public string Spell2Icon
        {
            get => _spell2Icon;
            set => SetProperty(ref _spell2Icon, value);
        }

        public string DisplayName
        {
            get => _displayName;
            set => SetProperty(ref _displayName, value);
        }

        public string PositionText
        {
            get => _positionText;
            set => SetProperty(ref _positionText, value);
        }

        public string RankText
        {
            get => _rankText;
            set => SetProperty(ref _rankText, value);
        }

        public string RecentRecordText
        {
            get => _recentRecordText;
            set => SetProperty(ref _recentRecordText, value);
        }

        public string KdaText
        {
            get => _kdaText;
            set => SetProperty(ref _kdaText, value);
        }

        public string StatusText
        {
            get => _statusText;
            set => SetProperty(ref _statusText, value);
        }

        public ObservableCollection<RecentMatchResultViewModel> RecentResults { get; }

        public ObservableCollection<LiveMatchRecentMatchViewModel> RecentMatches { get; }

        public bool IsLocalPlayer
        {
            get => _isLocalPlayer;
            set => SetProperty(ref _isLocalPlayer, value);
        }

        public bool IsHidden
        {
            get => _isHidden;
            set => SetProperty(ref _isHidden, value);
        }

        public bool IsPlaceholder
        {
            get => _isPlaceholder;
            set => SetProperty(ref _isPlaceholder, value);
        }

        public bool IsLoading
        {
            get => _isLoading;
            set => SetProperty(ref _isLoading, value);
        }

        public bool HasError
        {
            get => _hasError;
            set => SetProperty(ref _hasError, value);
        }

        public bool HasPerformanceData
        {
            get => _hasPerformanceData;
            set => SetProperty(ref _hasPerformanceData, value);
        }

        public bool HasRecentMatchDetails
        {
            get => _hasRecentMatchDetails;
            set => SetProperty(ref _hasRecentMatchDetails, value);
        }

        public bool CanOpenProfile
        {
            get => _canOpenProfile;
            set => SetProperty(ref _canOpenProfile, value);
        }

        public bool IsMyTeam
        {
            get => _isMyTeam;
            set => SetProperty(ref _isMyTeam, value);
        }

        public bool IsSelected
        {
            get => _isSelected;
            set => SetProperty(ref _isSelected, value);
        }

        public int Slot
        {
            get => _slot;
            set => SetProperty(ref _slot, value);
        }

        public LiveMatchPlayerDataState DataState
        {
            get => _dataState;
            set => SetProperty(ref _dataState, value);
        }

        public string Puuid
        {
            get => _puuid;
            set => SetProperty(ref _puuid, value);
        }

        public SummonerAccount Summoner
        {
            get => _summoner;
            set => SetProperty(ref _summoner, value);
        }

        private int _streakCount;
        public int StreakCount
        {
            get => _streakCount;
            set => SetProperty(ref _streakCount, value);
        }

        private bool _streakIsWinning;
        public bool StreakIsWinning
        {
            get => _streakIsWinning;
            set => SetProperty(ref _streakIsWinning, value);
        }

        private string _streakText;
        public string StreakText
        {
            get => _streakText;
            set => SetProperty(ref _streakText, value);
        }

        private bool _hasStreak;
        public bool HasStreak
        {
            get => _hasStreak;
            set => SetProperty(ref _hasStreak, value);
        }
    }
}

using HandyControl.Controls;
using Prism.Commands;
using Prism.Events;
using Prism.Regions;
using Prometheus.Core.Events;
using Prometheus.Core.Logging;
using Prometheus.Core.Models;
using Prometheus.Core.Mvvm;
using Prometheus.Core.Tasks;
using Prometheus.Modules.Match.Controls;
using Prometheus.Services.Interfaces.Client;
using Serilog;
using Serilog.Events;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Threading;

namespace Prometheus.Modules.Match.ViewModels
{
    /// <summary>
    /// Read-only projection of <see cref="IMatchService.Current"/> for the live-match view.
    /// All roster discovery and enrichment is owned by <see cref="IMatchService"/>.
    /// </summary>
    public class MatchViewModel : RegionViewModelBase
    {
        private const int TeamSize = 5;

        private readonly IEventAggregator _eventAggregator;
        private readonly IMatchService _matchService;
        private readonly IGameService _gameService;
        private readonly IResourceService _resourceService;
        private readonly LatestValueDispatcher<(LiveMatchSnapshot Snapshot, long Generation)>
            _snapshotDispatcher;

        private long _appliedVersion = -1;
        private long _subscriptionGeneration;
        private LiveMatchSnapshot _snapshot = LiveMatchSnapshot.Empty;
        private CancellationTokenSource _playAgainCts;
        private int _activeMatchQueueId;
        private int _completedMatchQueueId;
        private bool _isCreatingPlayAgainLobby;
        private bool _isSubscribed;
        private bool _destroyed;

        /// <summary>
        /// Set when the user explicitly closes the docked detail bar, so incoming
        /// snapshots do not silently reopen it.
        /// </summary>
        private bool _detailsDismissed;

        public MatchViewModel(
            IRegionManager regionManager,
            IEventAggregator eventAggregator,
            IMatchService matchService,
            IGameService gameService,
            IResourceService resourceService) : base(regionManager)
        {
            _eventAggregator = eventAggregator ??
                throw new ArgumentNullException(nameof(eventAggregator));
            _matchService = matchService ?? throw new ArgumentNullException(nameof(matchService));
            _gameService = gameService ?? throw new ArgumentNullException(nameof(gameService));
            _resourceService = resourceService ??
                throw new ArgumentNullException(nameof(resourceService));
            _snapshotDispatcher = new LatestValueDispatcher<
                (LiveMatchSnapshot Snapshot, long Generation)>(
                action => Dispatch(action, DispatcherPriority.Background),
                pending =>
                {
                    if (IsActiveSubscription(pending.Generation))
                    {
                        ApplySnapshot(pending.Snapshot);
                    }
                });

            MyTeam = [];
            TheirTeam = [];
            PostGameMyTeamSlices = [];
            PostGameTheirTeamSlices = [];
            PostGameKillParticipationSlices = [];
            PostGameDamageShareSlices = [];
            PostGameGoldShareSlices = [];
            PostGamePlayers = [];
            RefreshCommand = new DelegateCommand(ExecuteRefresh);
            OpenPlayerCommand = new DelegateCommand<LiveMatchPlayerViewModel>(OpenPlayer);
            SelectPlayerCommand = new DelegateCommand<LiveMatchPlayerViewModel>(
                SelectPlayer);
            CloseDetailsCommand = new DelegateCommand(CloseDetails);
            PlayAgainCommand = new DelegateCommand(
                () => CreatePlayAgainLobbyAsync().Observe(
                    "Creating a play-again matchmade lobby"),
                CanPlayAgain);
            SelectPostGameMetricCommand = new DelegateCommand<string>(
                SelectPostGameMetric);

            Subscribe();
            ApplySnapshot(_matchService.Current ?? LiveMatchSnapshot.Empty, true);
        }

        public ObservableCollection<LiveMatchPlayerViewModel> MyTeam { get; }

        public ObservableCollection<LiveMatchPlayerViewModel> TheirTeam { get; }

        public ObservableCollection<DoughnutSlice> PostGameMyTeamSlices { get; }

        public ObservableCollection<DoughnutSlice> PostGameTheirTeamSlices { get; }

        public ObservableCollection<DoughnutSlice> PostGameKillParticipationSlices { get; }

        public ObservableCollection<DoughnutSlice> PostGameDamageShareSlices { get; }

        public ObservableCollection<DoughnutSlice> PostGameGoldShareSlices { get; }

        public ObservableCollection<PostGamePlayerRowViewModel> PostGamePlayers { get; }

        public DelegateCommand RefreshCommand { get; }

        public DelegateCommand<LiveMatchPlayerViewModel> OpenPlayerCommand { get; }

        public DelegateCommand<LiveMatchPlayerViewModel> SelectPlayerCommand { get; }

        public DelegateCommand CloseDetailsCommand { get; }

        public DelegateCommand PlayAgainCommand { get; }

        public DelegateCommand<string> SelectPostGameMetricCommand { get; }

        private bool _isPlayAgainVisible;
        public bool IsPlayAgainVisible
        {
            get => _isPlayAgainVisible;
            private set => SetProperty(ref _isPlayAgainVisible, value);
        }

        public string PlayAgainButtonText => _isCreatingPlayAgainLobby
            ? Text("Match.Live.PlayAgain.Creating", "Creating lobby")
            : Text("Match.Live.PlayAgain", "Play again");

        public string PlayAgainToolTip
        {
            get
            {
                if (_isCreatingPlayAgainLobby)
                {
                    return Text("Match.Live.PlayAgain.Creating",
                        "Creating lobby");
                }

                if (!IsSupportedQuickMatchQueue(_completedMatchQueueId))
                {
                    return Text("Match.Live.PlayAgain.Unsupported",
                        "Play again is not supported for this mode");
                }

                if (_snapshot.ConnectionState != ConnectionState.Connected)
                {
                    return Text("Match.Live.PlayAgain.Offline",
                        "League Client is not connected");
                }

                if (_snapshot.GameflowPhase != GameflowPhase.None)
                {
                    return Text("Match.Live.PlayAgain.Waiting",
                        "Waiting for League Client to finish post-game processing");
                }

                return string.Format(
                    Text("Match.Live.PlayAgain.Ready", "Create a {0} lobby"),
                    GetQueueDisplayName(_completedMatchQueueId));
            }
        }

        private LiveMatchPlayerViewModel _selectedPlayer;
        public LiveMatchPlayerViewModel SelectedPlayer
        {
            get => _selectedPlayer;
            private set
            {
                if (ReferenceEquals(_selectedPlayer, value))
                {
                    return;
                }

                if (_selectedPlayer is not null)
                {
                    _selectedPlayer.IsSelected = false;
                }

                if (SetProperty(ref _selectedPlayer, value))
                {
                    if (_selectedPlayer is not null)
                    {
                        _selectedPlayer.IsSelected = true;
                    }

                    RaisePropertyChanged(nameof(HasSelectedPlayer));
                }
            }
        }

        public bool HasSelectedPlayer => SelectedPlayer?.HasRecentMatchDetails == true;

        private bool _hasRoster;
        public bool HasRoster
        {
            get => _hasRoster;
            private set => SetProperty(ref _hasRoster, value);
        }

        private bool _showEmptyState = true;
        public bool ShowEmptyState
        {
            get => _showEmptyState;
            private set => SetProperty(ref _showEmptyState, value);
        }

        private bool _isPostGameVisible;
        public bool IsPostGameVisible
        {
            get => _isPostGameVisible;
            private set => SetProperty(ref _isPostGameVisible, value);
        }

        private bool _isPostGameLoading;
        public bool IsPostGameLoading
        {
            get => _isPostGameLoading;
            private set => SetProperty(ref _isPostGameLoading, value);
        }

        private bool _isPostGameVictory;
        public bool IsPostGameVictory
        {
            get => _isPostGameVictory;
            private set => SetProperty(ref _isPostGameVictory, value);
        }

        private string _pageTitle;
        public string PageTitle
        {
            get => _pageTitle;
            private set => SetProperty(ref _pageTitle, value);
        }

        private string _postGameResultText;
        public string PostGameResultText
        {
            get => _postGameResultText;
            private set => SetProperty(ref _postGameResultText, value);
        }

        private string _postGameDescription;
        public string PostGameDescription
        {
            get => _postGameDescription;
            private set => SetProperty(ref _postGameDescription, value);
        }

        private string _postGameKills;
        public string PostGameKills
        {
            get => _postGameKills;
            private set => SetProperty(ref _postGameKills, value);
        }

        private string _postGameDeaths;
        public string PostGameDeaths
        {
            get => _postGameDeaths;
            private set => SetProperty(ref _postGameDeaths, value);
        }

        private string _postGameAssists;
        public string PostGameAssists
        {
            get => _postGameAssists;
            private set => SetProperty(ref _postGameAssists, value);
        }

        private string _postGameModeText;
        public string PostGameModeText
        {
            get => _postGameModeText;
            private set => SetProperty(ref _postGameModeText, value);
        }

        private string _postGameDurationText;
        public string PostGameDurationText
        {
            get => _postGameDurationText;
            private set => SetProperty(ref _postGameDurationText, value);
        }

        private string _postGameGameIdText;
        public string PostGameGameIdText
        {
            get => _postGameGameIdText;
            private set => SetProperty(ref _postGameGameIdText, value);
        }

        private bool _hasPostGameTeamDetails;
        public bool HasPostGameTeamDetails
        {
            get => _hasPostGameTeamDetails;
            private set => SetProperty(ref _hasPostGameTeamDetails, value);
        }

        private string _postGameLocalChampionIcon;
        public string PostGameLocalChampionIcon
        {
            get => _postGameLocalChampionIcon;
            private set => SetProperty(ref _postGameLocalChampionIcon, value);
        }

        private string _postGameLocalChampionFallbackText;
        public string PostGameLocalChampionFallbackText
        {
            get => _postGameLocalChampionFallbackText;
            private set => SetProperty(ref _postGameLocalChampionFallbackText, value);
        }

        private string _postGameLocalKdaText;
        public string PostGameLocalKdaText
        {
            get => _postGameLocalKdaText;
            private set => SetProperty(ref _postGameLocalKdaText, value);
        }

        private string _postGameKillParticipationText;
        public string PostGameKillParticipationText
        {
            get => _postGameKillParticipationText;
            private set => SetProperty(ref _postGameKillParticipationText, value);
        }

        private string _postGameDamageShareText;
        public string PostGameDamageShareText
        {
            get => _postGameDamageShareText;
            private set => SetProperty(ref _postGameDamageShareText, value);
        }

        private string _postGameGoldShareText;
        public string PostGameGoldShareText
        {
            get => _postGameGoldShareText;
            private set => SetProperty(ref _postGameGoldShareText, value);
        }

        private string _postGameMetricLabel;
        public string PostGameMetricLabel
        {
            get => _postGameMetricLabel;
            private set => SetProperty(ref _postGameMetricLabel, value);
        }

        private string _postGameMyTeamTotalText;
        public string PostGameMyTeamTotalText
        {
            get => _postGameMyTeamTotalText;
            private set => SetProperty(ref _postGameMyTeamTotalText, value);
        }

        private string _postGameTheirTeamTotalText;
        public string PostGameTheirTeamTotalText
        {
            get => _postGameTheirTeamTotalText;
            private set => SetProperty(ref _postGameTheirTeamTotalText, value);
        }

        private PostGameMetric _selectedPostGameMetric = PostGameMetric.ChampionDamage;
        public bool IsPostGameDamageMetric =>
            _selectedPostGameMetric == PostGameMetric.ChampionDamage;

        public bool IsPostGameGoldMetric =>
            _selectedPostGameMetric == PostGameMetric.GoldEarned;

        public bool IsPostGameDamageTakenMetric =>
            _selectedPostGameMetric == PostGameMetric.DamageTaken;

        private string _matchContextText;
        public string MatchContextText
        {
            get => _matchContextText;
            private set => SetProperty(ref _matchContextText, value);
        }

        private string _phaseText;
        public string PhaseText
        {
            get => _phaseText;
            private set => SetProperty(ref _phaseText, value);
        }

        private string _dataStatusText;
        public string DataStatusText
        {
            get => _dataStatusText;
            private set => SetProperty(ref _dataStatusText, value);
        }

        private string _updatedText;
        public string UpdatedText
        {
            get => _updatedText;
            private set => SetProperty(ref _updatedText, value);
        }

        private string _emptyTitle;
        public string EmptyTitle
        {
            get => _emptyTitle;
            private set => SetProperty(ref _emptyTitle, value);
        }

        private string _emptyDescription;
        public string EmptyDescription
        {
            get => _emptyDescription;
            private set => SetProperty(ref _emptyDescription, value);
        }

        private string _myTeamStatusText;
        public string MyTeamStatusText
        {
            get => _myTeamStatusText;
            private set => SetProperty(ref _myTeamStatusText, value);
        }

        private string _theirTeamStatusText;
        public string TheirTeamStatusText
        {
            get => _theirTeamStatusText;
            private set => SetProperty(ref _theirTeamStatusText, value);
        }

        public override void OnNavigatedTo(NavigationContext navigationContext)
        {
            Subscribe();
            var generation = _subscriptionGeneration;
            Dispatch(() =>
            {
                if (IsActiveSubscription(generation))
                {
                    ApplySnapshot(_matchService.Current ?? LiveMatchSnapshot.Empty, true);
                }
            });
        }

        public override void OnNavigatedFrom(NavigationContext navigationContext)
        {
            Unsubscribe();
            base.OnNavigatedFrom(navigationContext);
        }

        public override void Destroy()
        {
            _destroyed = true;
            _playAgainCts?.Cancel();
            Unsubscribe();
            base.Destroy();
        }

        private void Subscribe()
        {
            if (_destroyed || _isSubscribed)
            {
                return;
            }

            _matchService.SnapshotChanged += HandleSnapshotChanged;
            _eventAggregator.GetEvent<LanguageSwitchedEvent>().Subscribe(HandleLanguageSwitched);
            _isSubscribed = true;
            _subscriptionGeneration++;
        }

        private void Unsubscribe()
        {
            if (!_isSubscribed)
            {
                return;
            }

            _isSubscribed = false;
            _subscriptionGeneration++;
            _matchService.SnapshotChanged -= HandleSnapshotChanged;
            _eventAggregator.GetEvent<LanguageSwitchedEvent>().Unsubscribe(HandleLanguageSwitched);
        }

        private void HandleSnapshotChanged(object sender, LiveMatchSnapshotChangedEventArgs args)
        {
            _snapshotDispatcher.Publish((
                args?.Snapshot ?? LiveMatchSnapshot.Empty,
                _subscriptionGeneration));
        }

        private void HandleLanguageSwitched()
        {
            var generation = _subscriptionGeneration;
            Dispatch(() =>
            {
                if (IsActiveSubscription(generation))
                {
                    ApplySnapshot(_matchService.Current ?? LiveMatchSnapshot.Empty, true);
                }
            });
        }

        private void ApplySnapshot(LiveMatchSnapshot snapshot, bool allowSameVersion = false)
        {
            if (_destroyed)
            {
                return;
            }

            snapshot ??= LiveMatchSnapshot.Empty;
            if (snapshot.Version < _appliedVersion ||
                (!allowSameVersion && snapshot.Version == _appliedVersion))
            {
                return;
            }

            _appliedVersion = snapshot.Version;
            _snapshot = snapshot;
            UpdatePlayAgainContext(snapshot);
            var previousSelection = SelectedPlayer;
            var roster = snapshot.Roster;
            var hasIncomingRoster = (roster?.MyTeam?.Count ?? 0) > 0 ||
                (roster?.TheirTeam?.Count ?? 0) > 0;
            if (!hasIncomingRoster)
            {
                // Roster cleared (match ended): allow the next match to auto-open.
                _detailsDismissed = false;
            }
            var myTeam = (roster?.MyTeam ?? Array.Empty<LiveMatchPlayerSnapshot>())
                .Select(source => CreatePlayer(source, true))
                .ToArray();
            var theirTeam = (roster?.TheirTeam ?? Array.Empty<LiveMatchPlayerSnapshot>())
                .Select(source => CreatePlayer(source, false))
                .ToArray();

            Replace(MyTeam, myTeam);
            Replace(TheirTeam, theirTeam);
            SelectedPlayer = _detailsDismissed
                ? null
                : FindSelectedPlayer(previousSelection, myTeam, theirTeam);

            HasRoster = myTeam.Length > 0 || theirTeam.Length > 0;
            UpdatePostGamePresentation(snapshot);
            ShowEmptyState = !HasRoster && !IsPostGameVisible;
            MatchContextText = BuildMatchContext(snapshot);
            PhaseText = GetPhaseText(snapshot);
            EmptyTitle = Text("Match.Live.Empty.Title", "No live roster");
            EmptyDescription = Text("Match.Live.Empty.Description",
                "Enter champion select or a game to view both teams.");
            UpdatedText = snapshot.UpdatedAt == default
                ? Text("Match.Live.Updated.Unknown", "Not updated yet")
                : string.Format(Text("Match.Live.Updated", "Updated {0}"),
                    snapshot.UpdatedAt.ToLocalTime().ToString("HH:mm:ss"));
            MyTeamStatusText = GetTeamStatusText(myTeam, roster?.IsResolving == true);
            TheirTeamStatusText = GetTeamStatusText(theirTeam, roster?.IsResolving == true);
            DataStatusText = IsPostGameVisible
                ? IsPostGameLoading
                    ? Text("Match.Live.PostGame.Loading.Short", "Generating report")
                    : Text("Match.Live.PostGame.Ready", "Report ready")
                : GetDataStatusText(snapshot, myTeam.Concat(theirTeam).ToArray());
        }

        private LiveMatchPlayerViewModel CreatePlayer(
            LiveMatchPlayerSnapshot source, bool isMyTeam)
        {
            source ??= new LiveMatchPlayerSnapshot();
            var isHidden = source.IsHidden ||
                source.DataState == LiveMatchPlayerDataState.Hidden;
            var isPlaceholder = source.IsPlaceholder ||
                source.DataState == LiveMatchPlayerDataState.Placeholder;
            var isLoaded = source.DataState == LiveMatchPlayerDataState.Loaded;
            var recentCount = Math.Max(0, source.RecentMatchCount);
            var winRate = recentCount == 0
                ? 0
                : (int)Math.Round(source.RecentWins * 100d / recentCount,
                    MidpointRounding.AwayFromZero);
            var navigationSummoner = GetNavigationSummoner(
                source, isHidden, isPlaceholder);

            var player = new LiveMatchPlayerViewModel
            {
                ChampionIcon = source.ChampionIcon,
                Spell1Icon = source.Spell1Icon,
                Spell2Icon = source.Spell2Icon,
                DisplayName = isHidden
                    ? Text("Match.Live.Player.Hidden", "Hidden player")
                    : FormatDisplayName(source),
                PositionText = FormatPosition(source.Position),
                RankText = isLoaded ? FormatRank(source.SoloRank) : "--",
                RecentRecordText = isLoaded
                    ? string.Format(Text("Match.Live.Record.Format", "{0}W {1}L · {2}%"),
                        source.RecentWins, source.RecentLosses, winRate)
                    : "--",
                KdaText = isLoaded
                    ? string.Format(Text("Match.Live.Kda.Format", "KDA {0:0.0}"),
                        source.AverageKda)
                    : "KDA --",
                StatusText = GetPlayerStatusText(source, isHidden, isLoaded, recentCount),
                IsLocalPlayer = source.IsLocalPlayer,
                IsHidden = isHidden,
                IsPlaceholder = isPlaceholder,
                IsLoading = source.DataState == LiveMatchPlayerDataState.Loading,
                HasError = source.DataState == LiveMatchPlayerDataState.Error,
                HasPerformanceData = isLoaded && recentCount > 0,
                CanOpenProfile = navigationSummoner is not null,
                IsMyTeam = isMyTeam,
                Slot = source.Slot,
                DataState = source.DataState,
                Puuid = source.Puuid ?? string.Empty,
                Summoner = navigationSummoner
            };

            // RecentResults and RecentMatches share the same newest-first ordering,
            // so the strip segment at index i describes RecentMatches[i].
            var recentMatchDetails = source.RecentMatches;
            var resultIndex = 0;
            foreach (var result in source.RecentResults ?? Array.Empty<bool>())
            {
                player.RecentResults.Add(new RecentMatchResultViewModel
                {
                    IsWin = result,
                    ResultTooltip = BuildStripTooltip(
                        resultIndex,
                        result,
                        recentMatchDetails is not null &&
                            resultIndex < recentMatchDetails.Count
                            ? recentMatchDetails[resultIndex]
                            : null)
                });
                resultIndex++;
            }

            var matchIndex = 0;
            foreach (var recentMatch in source.RecentMatches ??
                Array.Empty<LiveMatchRecentMatchSnapshot>())
            {
                if (recentMatch is null)
                {
                    continue;
                }

                matchIndex++;
                var resultText = recentMatch.IsWin
                    ? Text("Match.Live.RecentDetails.Win", "Win")
                    : Text("Match.Live.RecentDetails.Loss", "Loss");
                var gameModeText = string.IsNullOrWhiteSpace(recentMatch.GameMode)
                    ? "--"
                    : recentMatch.GameMode;
                player.RecentMatches.Add(new LiveMatchRecentMatchViewModel
                {
                    GameId = recentMatch.GameId,
                    IndexText = $"#{matchIndex}",
                    ResultText = resultText,
                    GameModeText = gameModeText,
                    ChampionIcon = recentMatch.ChampionIcon ?? string.Empty,
                    ChampionFallbackText = recentMatch.ChampionId > 0
                        ? $"#{recentMatch.ChampionId}"
                        : "--",
                    Kills = recentMatch.Kills,
                    Deaths = recentMatch.Deaths,
                    Assists = recentMatch.Assists,
                    IsWin = recentMatch.IsWin,
                    AutomationText = string.Format(
                        Text("Match.Live.RecentDetails.Automation",
                            "Match {0}, {1}, K/D/A {2}/{3}/{4}, {5}"),
                        matchIndex, resultText, recentMatch.Kills,
                        recentMatch.Deaths, recentMatch.Assists, gameModeText)
                });
            }
            player.HasRecentMatchDetails = player.RecentMatches.Count > 0;

            CalculateStreak(player, source);

            return player;
        }

        private static LiveMatchPlayerViewModel FindSelectedPlayer(
            LiveMatchPlayerViewModel previousSelection,
            IReadOnlyList<LiveMatchPlayerViewModel> myTeam,
            IReadOnlyList<LiveMatchPlayerViewModel> theirTeam)
        {
            var players = myTeam.Concat(theirTeam).ToArray();
            LiveMatchPlayerViewModel selected = null;
            if (previousSelection is not null)
            {
                if (!string.IsNullOrWhiteSpace(previousSelection.Puuid))
                {
                    selected = players.FirstOrDefault(player =>
                        string.Equals(player.Puuid, previousSelection.Puuid,
                            StringComparison.Ordinal));
                }

                selected ??= players.FirstOrDefault(player =>
                    player.IsMyTeam == previousSelection.IsMyTeam &&
                    player.Slot == previousSelection.Slot);
            }

            if (selected?.HasRecentMatchDetails == true)
            {
                return selected;
            }

            return players.FirstOrDefault(player =>
                    player.IsLocalPlayer && player.HasRecentMatchDetails) ??
                players.FirstOrDefault(player => player.HasRecentMatchDetails);
        }

        private string GetPlayerStatusText(
            LiveMatchPlayerSnapshot source,
            bool isHidden,
            bool isLoaded,
            int recentCount)
        {
            if (isHidden)
            {
                return Text("Match.Live.Player.Hidden.Description",
                    "Information will be available after the game starts");
            }

            return source.DataState switch
            {
                LiveMatchPlayerDataState.Loading =>
                    Text("Match.Live.Player.Loading", "Loading player data"),
                LiveMatchPlayerDataState.Error =>
                    Text("Match.Live.Player.Error", "Unable to load player data"),
                LiveMatchPlayerDataState.Unavailable =>
                    Text("Match.Live.Player.Unavailable", "Player data unavailable"),
                _ when isLoaded && recentCount == 0 =>
                    Text("Match.Live.Player.NoData", "No recent data"),
                _ => string.Empty
            };
        }

        private string FormatDisplayName(LiveMatchPlayerSnapshot player)
        {
            var summoner = player.Summoner;
            if (summoner is not null)
            {
                var gameName = FirstNotEmpty(summoner.GameName, summoner.DisplayName,
                    summoner.SummonerName);
                if (!string.IsNullOrWhiteSpace(gameName))
                {
                    return string.IsNullOrWhiteSpace(summoner.TagLine)
                        ? gameName
                        : $"{gameName}#{summoner.TagLine}";
                }
            }

            if (!string.IsNullOrWhiteSpace(player.DisplayName))
            {
                return player.DisplayName;
            }

            return player.IsLocalPlayer
                ? Text("Match.Live.LocalPlayer", "You")
                : Text("Match.Live.Player.Unknown", "Unknown player");
        }

        private string FormatRank(Rank rank)
        {
            if (rank is null || rank.Tier == Tier.UNRANKED)
            {
                return Text("Match.Live.Rank.Unranked", "Unranked");
            }

            var tierKey = rank.Tier switch
            {
                Tier.IRON => "Career.Rank.Tier.Iron",
                Tier.BRONZE => "Career.Rank.Tier.Bronze",
                Tier.SILVER => "Career.Rank.Tier.Silver",
                Tier.GOLD => "Career.Rank.Tier.Gold",
                Tier.PLATINUM => "Career.Rank.Tier.Platinum",
                Tier.EMERALD => "Career.Rank.Tier.Emerald",
                Tier.DIAMOND => "Career.Rank.Tier.Diamond",
                Tier.MASTER => "Career.Rank.Tier.Master",
                Tier.GRANDMASTER => "Career.Rank.Tier.Grandmaster",
                Tier.CHALLENGER => "Career.Rank.Tier.Challenger",
                _ => "Career.Rank.Tier.Unranked"
            };
            var tier = Text(tierKey, rank.Tier.ToString());
            var division = string.IsNullOrWhiteSpace(rank.Division) ||
                string.Equals(rank.Division, nameof(Division.NA),
                    StringComparison.OrdinalIgnoreCase)
                ? string.Empty
                : $" {rank.Division}";
            return $"{tier}{division} · {rank.LeaguePoints} LP";
        }

        private string FormatPosition(string position)
        {
            var (key, fallback) = NormalizePosition(position) switch
            {
                "TOP" => ("Match.Live.Position.Top", "Top"),
                "JUNGLE" => ("Match.Live.Position.Jungle", "Jungle"),
                "MIDDLE" => ("Match.Live.Position.Middle", "Middle"),
                "BOTTOM" => ("Match.Live.Position.Bottom", "Bottom"),
                "UTILITY" => ("Match.Live.Position.Utility", "Support"),
                _ => ("Match.Live.Position.Unknown",
                    string.IsNullOrWhiteSpace(position) ? "--" : position)
            };
            return Text(key, fallback);
        }

        private string BuildMatchContext(LiveMatchSnapshot snapshot)
        {
            var parts = new[]
            {
                snapshot.PostGame?.GameMode,
                snapshot.GameflowSession?.GameData?.GameMode,
                snapshot.GameflowSession?.Map?.Name,
                snapshot.Matchmaking?.Queue?.Name
            }
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
            return parts.Length == 0
                ? Text("Match.Live.Context.Default", "Live match")
                : string.Join(" · ", parts);
        }

        private string GetPhaseText(LiveMatchSnapshot snapshot)
        {
            if (snapshot.ConnectionState is ConnectionState.Disconnected or
                ConnectionState.Stopping)
            {
                return Text("Match.Live.Phase.Offline", "Offline");
            }

            if (snapshot.ConnectionState is ConnectionState.Connecting or
                ConnectionState.Reconnecting)
            {
                return Text("Match.Live.Phase.Loading", "Connecting");
            }

            return snapshot.GameflowPhase switch
            {
                GameflowPhase.ChampSelect =>
                    Text("Match.Live.Phase.ChampionSelect", "Champion select"),
                GameflowPhase.GameStart or GameflowPhase.InProgress =>
                    Text("Match.Live.Phase.InProgress", "In game"),
                GameflowPhase.Reconnect =>
                    Text("Match.Live.Phase.Reconnect", "Reconnect"),
                GameflowPhase.WaitingForStats or GameflowPhase.PreEndOfGame or
                    GameflowPhase.EndOfGame =>
                    Text("Match.Live.Phase.PostGame", "Post game"),
                _ => Text("Match.Live.Phase.Unknown", "Waiting for a match")
            };
        }

        private string GetTeamStatusText(
            IReadOnlyCollection<LiveMatchPlayerViewModel> team,
            bool rosterIsResolving)
        {
            if (team.Count == 0)
            {
                return Text("Match.Live.Team.Unavailable", "Unavailable");
            }

            var loaded = team.Count(player =>
                player.DataState == LiveMatchPlayerDataState.Loaded);
            if (team.All(player => player.IsHidden))
            {
                return Text("Match.Live.Team.Hidden", "Identity hidden");
            }

            if (rosterIsResolving || team.Any(player => player.IsLoading))
            {
                return loaded > 0
                    ? string.Format(Text("Match.Live.Team.LoadingCount",
                        "Loading · {0}/5"), loaded)
                    : Text("Match.Live.Team.Loading", "Loading");
            }

            if (loaded >= TeamSize)
            {
                return Text("Match.Live.Team.Ready", "Ready");
            }

            return loaded > 0
                ? string.Format(Text("Match.Live.Team.Partial", "{0}/5 loaded"), loaded)
                : Text("Match.Live.Team.Unavailable", "Unavailable");
        }

        private string GetDataStatusText(
            LiveMatchSnapshot snapshot,
            IReadOnlyCollection<LiveMatchPlayerViewModel> players)
        {
            var loaded = players.Count(player =>
                player.DataState == LiveMatchPlayerDataState.Loaded);
            var total = players.Count;
            if (snapshot.DataQuality == DataQuality.Stale)
            {
                return total > 0
                    ? string.Format(Text("Match.Live.Data.StaleCount",
                        "Stale · {0}/{1}"), loaded, total)
                    : Text("Match.Live.Data.Stale", "Stale data");
            }

            if (snapshot.ConnectionState is ConnectionState.Disconnected or
                ConnectionState.Stopping or ConnectionState.Error ||
                snapshot.DataQuality == DataQuality.Error)
            {
                return Text("Match.Live.Data.Unavailable", "Unavailable");
            }

            if (snapshot.ConnectionState is ConnectionState.Connecting or
                ConnectionState.Reconnecting || snapshot.Roster?.IsResolving == true ||
                players.Any(player => player.IsLoading))
            {
                return total > 0
                    ? string.Format(Text("Match.Live.Data.LoadingCount",
                        "Loading · {0}/{1}"), loaded, total)
                    : Text("Match.Live.Data.Loading", "Loading");
            }

            if (total == 0)
            {
                return snapshot.DataQuality == DataQuality.Partial
                    ? Text("Match.Live.Data.Partial", "Partial data")
                    : Text("Match.Live.Data.Unavailable", "Unavailable");
            }

            if (snapshot.DataQuality == DataQuality.Partial ||
                players.Any(player =>
                    player.DataState != LiveMatchPlayerDataState.Loaded))
            {
                return string.Format(Text("Match.Live.Data.Count",
                    "Available {0}/{1}"), loaded, total);
            }

            return string.Format(Text("Match.Live.Data.FullCount",
                "Complete {0}/{1}"), loaded, total);
        }

        private void UpdatePostGamePresentation(LiveMatchSnapshot snapshot)
        {
            var postGamePhase = IsPostGamePhase(snapshot.GameflowPhase);
            var postGame = snapshot.PostGame;
            IsPostGameVisible = postGamePhase ||
                (snapshot.GameflowPhase == GameflowPhase.None && postGame is not null);
            IsPostGameLoading = IsPostGameVisible && postGame?.LocalPlayer is null;
            PageTitle = IsPostGameVisible
                ? Text("Match.Live.PostGame.Title", "Match result")
                : Text("Match.Live.Title", "Live match");

            if (!IsPostGameVisible)
            {
                ClearPostGameTeamPresentation();
                IsPostGameVictory = false;
                PostGameResultText = string.Empty;
                PostGameDescription = string.Empty;
                PostGameKills = string.Empty;
                PostGameDeaths = string.Empty;
                PostGameAssists = string.Empty;
                PostGameModeText = string.Empty;
                PostGameDurationText = string.Empty;
                PostGameGameIdText = string.Empty;
                return;
            }

            if (IsPostGameLoading)
            {
                ClearPostGameTeamPresentation();
                IsPostGameVictory = false;
                PostGameResultText = Text("Match.Live.PostGame.Loading",
                    "Generating match report");
                PostGameDescription = Text("Match.Live.PostGame.Loading.Description",
                    "The League Client is preparing this match's result.");
                PostGameKills = "--";
                PostGameDeaths = "--";
                PostGameAssists = "--";
                PostGameModeText = "--";
                PostGameDurationText = "--";
                PostGameGameIdText = string.Empty;
                return;
            }

            var player = postGame.LocalPlayer;
            IsPostGameVictory = player.Won;
            PostGameResultText = Text(player.Won
                ? "Match.Live.PostGame.Victory"
                : "Match.Live.PostGame.Defeat", player.Won ? "Victory" : "Defeat");
            PostGameDescription = Text("Match.Live.PostGame.Description",
                "Your match report is ready.");
            PostGameKills = player.Kills.ToString();
            PostGameDeaths = player.Deaths.ToString();
            PostGameAssists = player.Assists.ToString();
            PostGameModeText = GetPostGameModeDisplayName(postGame);
            var duration = TimeSpan.FromSeconds(Math.Max(0, postGame.GameLength));
            PostGameDurationText = postGame.GameLength <= 0
                ? "--"
                : duration.TotalHours >= 1
                    ? duration.ToString(@"h\:mm\:ss")
                    : duration.ToString(@"mm\:ss");
            PostGameGameIdText = postGame.GameId > 0
                ? string.Format(Text("Match.Live.PostGame.GameId", "Game {0}"),
                    postGame.GameId)
                : string.Empty;
            BuildPostGameTeamPresentation(postGame);
        }

        private void SelectPostGameMetric(string metricName)
        {
            if (!Enum.TryParse(metricName, out PostGameMetric metric) ||
                metric == _selectedPostGameMetric)
            {
                return;
            }

            _selectedPostGameMetric = metric;
            RaisePropertyChanged(nameof(IsPostGameDamageMetric));
            RaisePropertyChanged(nameof(IsPostGameGoldMetric));
            RaisePropertyChanged(nameof(IsPostGameDamageTakenMetric));
            if (_snapshot.PostGame is not null)
            {
                BuildPostGameTeamPresentation(_snapshot.PostGame);
            }
        }

        private void BuildPostGameTeamPresentation(PostGameSnapshot postGame)
        {
            var localPlayer = postGame.LocalPlayer;
            var teams = (postGame.Teams ?? [])
                .Where(team => team is not null)
                .ToArray();
            var myTeam = teams.FirstOrDefault(team =>
                (team.Players ?? []).Any(player => MatchesLocalPlayer(
                    player, localPlayer)));
            myTeam ??= localPlayer is null
                ? null
                : teams.FirstOrDefault(team => team.Won == localPlayer.Won);
            var theirTeam = teams.FirstOrDefault(team =>
                !ReferenceEquals(team, myTeam));
            var myPlayers = (myTeam?.Players ?? [])
                .Where(player => player is not null)
                .Take(TeamSize)
                .ToArray();
            var theirPlayers = (theirTeam?.Players ?? [])
                .Where(player => player is not null)
                .Take(TeamSize)
                .ToArray();
            var teamLocalPlayer = myPlayers.FirstOrDefault(player =>
                MatchesLocalPlayer(player, localPlayer));
            var displayLocalPlayer = teamLocalPlayer ?? localPlayer;

            HasPostGameTeamDetails = myPlayers.Length > 0 || theirPlayers.Length > 0;
            PostGameLocalChampionIcon = FirstNotEmpty(
                displayLocalPlayer?.ChampionIcon,
                localPlayer?.ChampionIcon);
            PostGameLocalChampionFallbackText = FirstNotEmpty(
                displayLocalPlayer?.ChampionName,
                localPlayer?.ChampionName,
                displayLocalPlayer?.ChampionId > 0
                    ? $"#{displayLocalPlayer.ChampionId}"
                    : "--");
            PostGameLocalKdaText = displayLocalPlayer is null
                ? "--"
                : $"{displayLocalPlayer.Kills} / {displayLocalPlayer.Deaths} / " +
                  $"{displayLocalPlayer.Assists} · KDA " +
                  $"{CalculateKda(displayLocalPlayer):0.0}";

            BuildPostGameSummaryRings(displayLocalPlayer, myPlayers);
            BuildPostGameMetricSlices(myPlayers, theirPlayers);
            var rows = BuildPostGamePlayerRows(myPlayers, true)
                .Concat(BuildPostGamePlayerRows(theirPlayers, false));
            Replace(PostGamePlayers, rows);
        }

        private void BuildPostGameSummaryRings(
            PostGamePlayerSnapshot localPlayer,
            IReadOnlyCollection<PostGamePlayerSnapshot> myTeam)
        {
            var teamKills = myTeam.Sum(player => Math.Max(0, player.Kills));
            var teamDamage = myTeam.Sum(player =>
                Math.Max(0, player.Stats?.TotalDamageDealtToChampions ?? 0));
            var teamGold = myTeam.Sum(player =>
                Math.Max(0, player.Stats?.GoldEarned ?? 0));
            var killParticipation = localPlayer is null || teamKills <= 0
                ? (double?)null
                : (localPlayer.Kills + localPlayer.Assists) * 100d / teamKills;
            var damageShare = localPlayer?.Stats is null || teamDamage <= 0
                ? (double?)null
                : localPlayer.Stats.TotalDamageDealtToChampions * 100d / teamDamage;
            var goldShare = localPlayer?.Stats is null || teamGold <= 0
                ? (double?)null
                : localPlayer.Stats.GoldEarned * 100d / teamGold;

            SetPostGameProgress(PostGameKillParticipationSlices,
                killParticipation, 0, out var killParticipationText);
            SetPostGameProgress(PostGameDamageShareSlices,
                damageShare, 1, out var damageShareText);
            SetPostGameProgress(PostGameGoldShareSlices,
                goldShare, 2, out var goldShareText);
            PostGameKillParticipationText = killParticipationText;
            PostGameDamageShareText = damageShareText;
            PostGameGoldShareText = goldShareText;
        }

        private void BuildPostGameMetricSlices(
            IReadOnlyCollection<PostGamePlayerSnapshot> myTeam,
            IReadOnlyCollection<PostGamePlayerSnapshot> theirTeam)
        {
            PostGameMetricLabel = _selectedPostGameMetric switch
            {
                PostGameMetric.GoldEarned =>
                    Text("Match.Live.PostGame.Metric.Gold", "Gold earned"),
                PostGameMetric.DamageTaken =>
                    Text("Match.Live.PostGame.Metric.DamageTaken", "Damage taken"),
                _ => Text("Match.Live.PostGame.Metric.ChampionDamage",
                    "Champion damage")
            };
            var mySlices = CreatePostGameSlices(myTeam);
            var theirSlices = CreatePostGameSlices(theirTeam);
            Replace(PostGameMyTeamSlices, mySlices);
            Replace(PostGameTheirTeamSlices, theirSlices);
            PostGameMyTeamTotalText = FormatPostGameValue(
                mySlices.Sum(slice => slice.Value));
            PostGameTheirTeamTotalText = FormatPostGameValue(
                theirSlices.Sum(slice => slice.Value));
        }

        private DoughnutSlice[] CreatePostGameSlices(
            IReadOnlyCollection<PostGamePlayerSnapshot> players)
        {
            var values = players
                .Select((player, index) => new
                {
                    Player = player,
                    Index = index,
                    Value = GetPostGameMetricValue(player)
                })
                .Where(item => item.Value > 0)
                .ToArray();
            var total = values.Sum(item => item.Value);
            return values.Select(item => new DoughnutSlice
            {
                DisplayName = GetPostGamePlayerName(item.Player, item.Index),
                Value = item.Value,
                ValueText = FormatPostGameValue(item.Value),
                PercentageText = total <= 0
                    ? "--"
                    : $"{item.Value * 100d / total:0}%",
                PaletteIndex = item.Index,
                IsLocalPlayer = item.Player.IsLocalPlayer ||
                    MatchesLocalPlayer(item.Player, _snapshot.PostGame?.LocalPlayer)
            }).ToArray();
        }

        private IEnumerable<PostGamePlayerRowViewModel> BuildPostGamePlayerRows(
            IReadOnlyCollection<PostGamePlayerSnapshot> players,
            bool isMyTeam)
        {
            var teamDamage = players.Sum(player =>
                Math.Max(0, player.Stats?.TotalDamageDealtToChampions ?? 0));
            return players.Select((player, index) => new PostGamePlayerRowViewModel
            {
                ChampionIcon = player.ChampionIcon ?? string.Empty,
                ChampionFallbackText = FirstNotEmpty(player.ChampionName,
                    player.ChampionId > 0 ? $"#{player.ChampionId}" : "--"),
                DisplayName = GetPostGamePlayerName(player, index),
                KdaText = $"{player.Kills} / {player.Deaths} / {player.Assists}",
                GoldText = FormatPostGameValue(player.Stats?.GoldEarned ?? 0),
                CreepScoreText = ((player.Stats?.MinionsKilled ?? 0) +
                    (player.Stats?.NeutralMinionsKilled ?? 0)).ToString(),
                ChampionDamageText = FormatPostGameValue(
                    player.Stats?.TotalDamageDealtToChampions ?? 0),
                DamageTakenText = FormatPostGameValue(
                    player.Stats?.TotalDamageTaken ?? 0),
                VisionScoreText = (player.Stats?.VisionScore ?? 0).ToString(),
                TeamShareText = teamDamage <= 0
                    ? "--"
                    : $"{(player.Stats?.TotalDamageDealtToChampions ?? 0) *
                        100d / teamDamage:0}%",
                IsMyTeam = isMyTeam,
                IsLocalPlayer = player.IsLocalPlayer ||
                    MatchesLocalPlayer(player, _snapshot.PostGame?.LocalPlayer)
            });
        }

        private double GetPostGameMetricValue(PostGamePlayerSnapshot player)
        {
            return _selectedPostGameMetric switch
            {
                PostGameMetric.GoldEarned => player.Stats?.GoldEarned ?? 0,
                PostGameMetric.DamageTaken => player.Stats?.TotalDamageTaken ?? 0,
                _ => player.Stats?.TotalDamageDealtToChampions ?? 0
            };
        }

        private void ClearPostGameTeamPresentation()
        {
            HasPostGameTeamDetails = false;
            PostGameLocalChampionIcon = string.Empty;
            PostGameLocalChampionFallbackText = string.Empty;
            PostGameLocalKdaText = string.Empty;
            PostGameKillParticipationText = "--";
            PostGameDamageShareText = "--";
            PostGameGoldShareText = "--";
            PostGameMetricLabel = string.Empty;
            PostGameMyTeamTotalText = "--";
            PostGameTheirTeamTotalText = "--";
            PostGameMyTeamSlices.Clear();
            PostGameTheirTeamSlices.Clear();
            PostGameKillParticipationSlices.Clear();
            PostGameDamageShareSlices.Clear();
            PostGameGoldShareSlices.Clear();
            PostGamePlayers.Clear();
        }

        private static void SetPostGameProgress(
            ObservableCollection<DoughnutSlice> target,
            double? percentage,
            int paletteIndex,
            out string percentageText)
        {
            target.Clear();
            if (percentage is null)
            {
                percentageText = "--";
                return;
            }

            var normalized = Math.Clamp(percentage.Value, 0d, 100d);
            percentageText = $"{normalized:0}%";
            if (normalized > 0)
            {
                target.Add(new DoughnutSlice
                {
                    Value = normalized,
                    PercentageText = percentageText,
                    PaletteIndex = paletteIndex
                });
            }
        }

        private static bool MatchesLocalPlayer(
            PostGamePlayerSnapshot player,
            PostGamePlayerSnapshot localPlayer)
        {
            if (player is null || localPlayer is null)
            {
                return false;
            }

            if (player.IsLocalPlayer)
            {
                return true;
            }

            if (!string.IsNullOrWhiteSpace(player.Puuid) &&
                string.Equals(player.Puuid, localPlayer.Puuid,
                    StringComparison.Ordinal))
            {
                return true;
            }

            if (player.SummonerId > 0 &&
                player.SummonerId == localPlayer.SummonerId)
            {
                return true;
            }

            return player.ChampionId == localPlayer.ChampionId &&
                !string.IsNullOrWhiteSpace(player.SummonerName) &&
                string.Equals(player.SummonerName, localPlayer.SummonerName,
                    StringComparison.OrdinalIgnoreCase);
        }

        private string GetPostGamePlayerName(PostGamePlayerSnapshot player, int index)
        {
            if (MatchesLocalPlayer(player, _snapshot.PostGame?.LocalPlayer))
            {
                return Text("Match.Live.PostGame.You", "You");
            }

            return FirstNotEmpty(player.SummonerName, player.ChampionName,
                string.Format(Text("Match.Live.PostGame.Player", "Player {0}"),
                    index + 1));
        }

        private static double CalculateKda(PostGamePlayerSnapshot player)
        {
            return (player.Kills + player.Assists) /
                (double)Math.Max(1, player.Deaths);
        }

        private static string FormatPostGameValue(double value)
        {
            return value >= 1000d
                ? $"{value / 1000d:0.#}k"
                : $"{value:0}";
        }

        private void UpdatePlayAgainContext(LiveMatchSnapshot snapshot)
        {
            var queueId = GetSnapshotQueueId(snapshot);
            if (snapshot.GameflowPhase is GameflowPhase.ChampSelect or
                GameflowPhase.GameStart or GameflowPhase.InProgress or
                GameflowPhase.Reconnect)
            {
                if (queueId > 0)
                {
                    _activeMatchQueueId = queueId;
                }

                _completedMatchQueueId = 0;
                IsPlayAgainVisible = false;
            }
            else if (IsPostGamePhase(snapshot.GameflowPhase))
            {
                _completedMatchQueueId = queueId > 0
                    ? queueId
                    : _activeMatchQueueId;
                IsPlayAgainVisible = true;
            }
            else if (snapshot.GameflowPhase is GameflowPhase.Lobby or
                GameflowPhase.Matchmaking or GameflowPhase.ReadyCheck)
            {
                _completedMatchQueueId = 0;
                IsPlayAgainVisible = false;
            }

            RaisePlayAgainStateChanged();
        }

        private bool CanPlayAgain()
        {
            return !_destroyed &&
                !_isCreatingPlayAgainLobby &&
                IsPlayAgainVisible &&
                IsSupportedQuickMatchQueue(_completedMatchQueueId) &&
                _snapshot.ConnectionState == ConnectionState.Connected &&
                _snapshot.GameflowPhase == GameflowPhase.None;
        }

        private async Task CreatePlayAgainLobbyAsync()
        {
            var operationId = Guid.NewGuid();
            var stopwatch = Stopwatch.StartNew();
            var queueId = _completedMatchQueueId;
            var connectionState = _snapshot.ConnectionState;
            var gameflowPhase = _snapshot.GameflowPhase;
            var queueName = GetQueueDisplayName(queueId);

            if (!CanPlayAgain())
            {
                var rejectionMessage = Text("Match.Live.PlayAgain.Unavailable",
                    "Play again is not available right now");
                WritePlayAgainOperation(
                    LogEventLevel.Warning,
                    "Rejected",
                    operationId,
                    queueId,
                    gameflowPhase,
                    connectionState,
                    stopwatch.ElapsedMilliseconds,
                    rejectionMessage,
                    "InvalidClientState");
                Growl.Warning(rejectionMessage);
                return;
            }

            _isCreatingPlayAgainLobby = true;
            RaisePlayAgainStateChanged();
            var cancellation = new CancellationTokenSource();
            _playAgainCts = cancellation;
            var level = LogEventLevel.Error;
            var outcome = "Failed";
            var message = Text("HomePage.QuickMatch.Failed",
                "Unable to create the matchmade lobby");
            string errorCode = "LobbyNotConfirmed";
            string errorType = null;
            Exception operationException = null;
            try
            {
                var result = await _gameService.CreateMatchmadeLobbyAsync(
                    queueId,
                    cancellation.Token);
                switch (result.Status)
                {
                    case MatchmadeLobbyCreationStatus.Created:
                        level = LogEventLevel.Information;
                        outcome = "Succeeded";
                        message = string.Format(
                            Text("HomePage.QuickMatch.Created",
                                "Entered the {0} lobby"),
                            queueName);
                        errorCode = null;
                        IsPlayAgainVisible = false;
                        break;
                    case MatchmadeLobbyCreationStatus.ClientUnavailable:
                        level = LogEventLevel.Warning;
                        outcome = "Rejected";
                        message = Text("HomePage.QuickMatch.Unavailable",
                            "The current client state cannot create a matchmade lobby");
                        errorCode = "ClientUnavailable";
                        break;
                    case MatchmadeLobbyCreationStatus.QueueUnavailable:
                        level = LogEventLevel.Warning;
                        outcome = "Rejected";
                        message = string.Format(
                            Text("HomePage.QuickMatch.QueueUnavailable",
                                "{0} is currently unavailable"),
                            queueName);
                        errorCode = "QueueUnavailable";
                        break;
                    case MatchmadeLobbyCreationStatus.OperationInProgress:
                        level = LogEventLevel.Warning;
                        outcome = "Rejected";
                        message = Text("HomePage.QuickMatch.Unavailable",
                            "The current client state cannot create a matchmade lobby");
                        errorCode = "OperationInProgress";
                        break;
                }
            }
            catch (OperationCanceledException)
            {
                level = LogEventLevel.Information;
                outcome = "Cancelled";
                message = Text("HomePage.QuickMatch.Cancelled",
                    "Creating the matchmade lobby was cancelled");
                errorCode = null;
            }
            catch (Exception exception)
            {
                errorCode = null;
                errorType = exception.GetType().Name;
                operationException = exception;
            }
            finally
            {
                if (ReferenceEquals(_playAgainCts, cancellation))
                {
                    _playAgainCts = null;
                }

                cancellation.Dispose();
                _isCreatingPlayAgainLobby = false;
                RaisePlayAgainStateChanged();
            }

            WritePlayAgainOperation(
                level,
                outcome,
                operationId,
                queueId,
                gameflowPhase,
                connectionState,
                stopwatch.ElapsedMilliseconds,
                message,
                errorCode,
                errorType,
                operationException);
            if (_destroyed)
            {
                return;
            }

            switch (outcome)
            {
                case "Succeeded":
                    Growl.Info(message);
                    break;
                case "Rejected":
                    Growl.Warning(message);
                    break;
                case "Failed":
                    Growl.Error(message);
                    break;
            }
        }

        private static void WritePlayAgainOperation(
            LogEventLevel level,
            string outcome,
            Guid operationId,
            int queueId,
            GameflowPhase gameflowPhase,
            ConnectionState connectionState,
            long durationMs,
            string displayMessage,
            string errorCode = null,
            string errorType = null,
            Exception exception = null)
        {
            var properties = new Dictionary<string, object>
            {
                ["QueueId"] = queueId,
                ["GameflowPhase"] = gameflowPhase.ToString(),
                ["ConnectionState"] = connectionState.ToString(),
                ["DurationMs"] = durationMs
            };
            if (!string.IsNullOrWhiteSpace(errorCode))
            {
                properties["ErrorCode"] = errorCode;
            }

            if (!string.IsNullOrWhiteSpace(errorType))
            {
                properties["ErrorType"] = errorType;
            }

            OperationLog.Write(
                level,
                "lobby.matchmade.create",
                "Lobby",
                "Manual",
                outcome,
                operationId,
                "Match",
                displayMessage,
                properties,
                exception);
        }

        private void RaisePlayAgainStateChanged()
        {
            RaisePropertyChanged(nameof(PlayAgainButtonText));
            RaisePropertyChanged(nameof(PlayAgainToolTip));
            PlayAgainCommand?.RaiseCanExecuteChanged();
        }

        private int GetSnapshotQueueId(LiveMatchSnapshot snapshot)
        {
            return snapshot.PostGame?.QueueId > 0
                ? snapshot.PostGame.QueueId
                : snapshot.GameflowSession?.GameData?.QueueId > 0
                    ? snapshot.GameflowSession.GameData.QueueId
                    : snapshot.Matchmaking?.Queue?.Id ?? 0;
        }

        private string GetQueueDisplayName(
            int queueId,
            string gameMode = null,
            int mapId = 0)
        {
            return GameModeResolver.Classify(queueId, gameMode, mapId) switch
            {
                GameModeKind.RankedSoloDuo =>
                    Text("HomePage.QuickMatch.SoloDuo", "Ranked Solo/Duo"),
                GameModeKind.RankedFlex =>
                    Text("HomePage.QuickMatch.Flex", "Ranked Flex"),
                GameModeKind.Aram =>
                    Text("HomePage.QuickMatch.Aram", "ARAM"),
                GameModeKind.HextechAram =>
                    Text("HomePage.QuickMatch.HextechAram", "ARAM Mayhem"),
                _ => Text("Match.Live.PlayAgain.UnknownMode", "this mode")
            };
        }

        private string GetPostGameModeDisplayName(PostGameSnapshot postGame)
        {
            // Preserve the mode string returned by the client for ordinary
            // completed matches (for example, CLASSIC).  The resolver is
            // still authoritative for the modes whose identifiers vary
            // between snapshots, especially Hextech ARAM.
            if (GameModeResolver.IsHextechAram(
                    postGame.QueueId,
                    postGame.GameMode))
            {
                return Text("HomePage.QuickMatch.HextechAram", "ARAM Mayhem");
            }

            if (!string.IsNullOrWhiteSpace(postGame.GameMode))
            {
                return postGame.GameMode;
            }

            return GetQueueDisplayName(
                postGame.QueueId,
                postGame.GameMode,
                postGame.MapId);
        }

        private static bool IsSupportedQuickMatchQueue(int queueId)
        {
            return GameModeResolver.IsQuickMatchQueue(queueId);
        }

        private static bool IsPostGamePhase(GameflowPhase phase)
        {
            return phase is GameflowPhase.WaitingForStats or
                GameflowPhase.PreEndOfGame or
                GameflowPhase.EndOfGame;
        }

        private async void ExecuteRefresh()
        {
            if (_destroyed)
            {
                return;
            }

            try
            {
                await _matchService.RefreshAsync();
            }
            catch (Exception exception)
            {
                Log.Error(exception, "Unable to refresh live-match data");
            }
        }

        private void OpenPlayer(LiveMatchPlayerViewModel player)
        {
            if (player?.CanOpenProfile != true || player.IsHidden ||
                player.IsPlaceholder || player.Summoner is null ||
                !HasPublicIdentity(player))
            {
                return;
            }

            _eventAggregator.GetEvent<SearchSummonerEvent>().Publish(player.Summoner);
        }

        private void SelectPlayer(LiveMatchPlayerViewModel player)
        {
            if (player?.HasRecentMatchDetails != true)
            {
                return;
            }

            // Clicking the already-selected player collapses the docked detail bar.
            if (ReferenceEquals(player, SelectedPlayer))
            {
                _detailsDismissed = true;
                SelectedPlayer = null;
                return;
            }

            _detailsDismissed = false;
            SelectedPlayer = player;
        }

        private void CloseDetails()
        {
            _detailsDismissed = true;
            SelectedPlayer = null;
        }

        private string Text(string key, string fallback)
        {
            try
            {
                return _resourceService.FindResource<string>(key) ?? fallback;
            }
            catch (Exception exception)
            {
                Log.Warning(exception, "Unable to resolve language resource {ResourceKey}", key);
                return fallback;
            }
        }

        private static bool HasPublicIdentity(LiveMatchPlayerSnapshot player)
        {
            return !string.IsNullOrWhiteSpace(player.Puuid) ||
                !string.IsNullOrWhiteSpace(player.Summoner?.Puuid);
        }

        private static bool HasPublicIdentity(LiveMatchPlayerViewModel player)
        {
            return !string.IsNullOrWhiteSpace(player.Puuid) ||
                !string.IsNullOrWhiteSpace(player.Summoner?.Puuid);
        }

        private static SummonerAccount GetNavigationSummoner(
            LiveMatchPlayerSnapshot player,
            bool isHidden,
            bool isPlaceholder)
        {
            if (isHidden || isPlaceholder || !HasPublicIdentity(player))
            {
                return null;
            }

            if (player.Summoner is not null)
            {
                return player.Summoner;
            }

            var displayName = player.DisplayName?.Trim() ?? string.Empty;
            var separator = displayName.LastIndexOf('#');
            var gameName = separator > 0 ? displayName[..separator] : displayName;
            var tagLine = separator > 0 && separator < displayName.Length - 1
                ? displayName[(separator + 1)..]
                : string.Empty;
            return new SummonerAccount
            {
                Puuid = player.Puuid,
                GameName = gameName,
                DisplayName = displayName,
                SummonerName = gameName,
                TagLine = tagLine
            };
        }

        private bool IsActiveSubscription(long generation)
        {
            return !_destroyed && _isSubscribed &&
                generation == _subscriptionGeneration;
        }

        private static string NormalizePosition(string position)
        {
            return position?.Trim().ToUpperInvariant() switch
            {
                "MID" => "MIDDLE",
                "JUG" => "JUNGLE",
                "ADC" or "BOT" => "BOTTOM",
                "SUPPORT" or "SUP" => "UTILITY",
                string value => value ?? string.Empty
            };
        }

        private static string FirstNotEmpty(params string[] values)
        {
            return values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ??
                string.Empty;
        }

        private static void Replace<T>(ObservableCollection<T> target, IEnumerable<T> values)
        {
            target.Clear();
            foreach (var value in values)
            {
                target.Add(value);
            }
        }

        private static void Dispatch(Action action,
            DispatcherPriority priority = DispatcherPriority.Normal)
        {
            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher is null || dispatcher.CheckAccess())
            {
                action();
                return;
            }

            dispatcher.BeginInvoke(priority, action);
        }

        /// <summary>
        /// Builds the hover text for one segment of the 20-slot result strip.
        /// Falls back to index + result when per-match detail is unavailable.
        /// </summary>
        private string BuildStripTooltip(
            int index,
            bool isWin,
            LiveMatchRecentMatchSnapshot detail)
        {
            var resultText = isWin
                ? Text("Match.Live.RecentDetails.Win", "Win")
                : Text("Match.Live.RecentDetails.Loss", "Loss");
            if (detail is null)
            {
                return string.Format(
                    Text("Match.Live.Strip.Tooltip.Short", "Match {0} · {1}"),
                    index + 1, resultText);
            }

            var gameModeText = string.IsNullOrWhiteSpace(detail.GameMode)
                ? "--"
                : detail.GameMode;
            return string.Format(
                Text("Match.Live.Strip.Tooltip", "Match {0} · {1} · {2}/{3}/{4} · {5}"),
                index + 1, resultText, detail.Kills, detail.Deaths,
                detail.Assists, gameModeText);
        }

        private void CalculateStreak(
            LiveMatchPlayerViewModel player,
            LiveMatchPlayerSnapshot source)
        {
            var results = source.RecentResults;
            if (results == null || results.Count < 2)
            {
                player.HasStreak = false;
                return;
            }

            // RecentResults[0] is the most recent match (see MatchService.LoadPlayerPerformanceAsync).
            var mostRecent = results[0];
            var count = 1;
            for (var i = 1; i < results.Count; i++)
            {
                if (results[i] != mostRecent)
                {
                    break;
                }

                count++;
            }

            if (count >= 3)
            {
                player.StreakCount = count;
                player.StreakIsWinning = mostRecent;
                player.HasStreak = true;
                var streakKey = mostRecent
                    ? "Match.Live.Streak.Win"
                    : "Match.Live.Streak.Loss";
                player.StreakText = string.Format(
                    Text(streakKey, mostRecent ? "{0} Win Streak" : "{0} Loss Streak"),
                    count);
            }
            else
            {
                player.HasStreak = false;
            }
        }
    }
}

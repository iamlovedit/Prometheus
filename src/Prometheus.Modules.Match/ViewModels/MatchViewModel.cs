using Prism.Commands;
using Prism.Events;
using Prism.Regions;
using Prometheus.Core.Events;
using Prometheus.Core.Models;
using Prometheus.Core.Mvvm;
using Prometheus.Services.Interfaces.Client;
using Serilog;
using System.Collections.ObjectModel;
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
        private readonly IResourceService _resourceService;
        private readonly LatestValueDispatcher<(LiveMatchSnapshot Snapshot, long Generation)>
            _snapshotDispatcher;

        private long _appliedVersion = -1;
        private long _subscriptionGeneration;
        private bool _isSubscribed;
        private bool _destroyed;

        public MatchViewModel(
            IRegionManager regionManager,
            IEventAggregator eventAggregator,
            IMatchService matchService,
            IResourceService resourceService) : base(regionManager)
        {
            _eventAggregator = eventAggregator ??
                throw new ArgumentNullException(nameof(eventAggregator));
            _matchService = matchService ?? throw new ArgumentNullException(nameof(matchService));
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
            RefreshCommand = new DelegateCommand(ExecuteRefresh);
            OpenPlayerCommand = new DelegateCommand<LiveMatchPlayerViewModel>(OpenPlayer);
            SelectPlayerCommand = new DelegateCommand<LiveMatchPlayerViewModel>(
                SelectPlayer);

            Subscribe();
            ApplySnapshot(_matchService.Current ?? LiveMatchSnapshot.Empty, true);
        }

        public ObservableCollection<LiveMatchPlayerViewModel> MyTeam { get; }

        public ObservableCollection<LiveMatchPlayerViewModel> TheirTeam { get; }

        public DelegateCommand RefreshCommand { get; }

        public DelegateCommand<LiveMatchPlayerViewModel> OpenPlayerCommand { get; }

        public DelegateCommand<LiveMatchPlayerViewModel> SelectPlayerCommand { get; }

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
            var previousSelection = SelectedPlayer;
            var roster = snapshot.Roster;
            var myTeam = (roster?.MyTeam ?? Array.Empty<LiveMatchPlayerSnapshot>())
                .Select(source => CreatePlayer(source, true))
                .ToArray();
            var theirTeam = (roster?.TheirTeam ?? Array.Empty<LiveMatchPlayerSnapshot>())
                .Select(source => CreatePlayer(source, false))
                .ToArray();

            Replace(MyTeam, myTeam);
            Replace(TheirTeam, theirTeam);
            SelectedPlayer = FindSelectedPlayer(previousSelection, myTeam, theirTeam);

            HasRoster = myTeam.Length > 0 || theirTeam.Length > 0;
            ShowEmptyState = !HasRoster;
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
            DataStatusText = GetDataStatusText(snapshot, myTeam.Concat(theirTeam).ToArray());
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

            foreach (var result in source.RecentResults ?? Array.Empty<bool>())
            {
                player.RecentResults.Add(new RecentMatchResultViewModel
                {
                    IsWin = result
                });
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
            if (player?.HasRecentMatchDetails == true)
            {
                SelectedPlayer = player;
            }
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
    }
}

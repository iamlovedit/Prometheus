using Prism.Events;
using Prism.Mvvm;
using Prometheus.Core.Events;
using Prometheus.Core.Models;
using Prometheus.Core.Mvvm;
using Prometheus.Desktop.Services;
using Prometheus.Services.Interfaces.Client;
using Serilog;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Threading;

namespace Prometheus.ViewModels
{
    public sealed class LcuCompanionRecentResultViewModel
    {
        public bool IsWin { get; init; }
    }

    public sealed class LcuCompanionPlayerViewModel
    {
        public string ChampionIcon { get; init; } = string.Empty;

        public string DisplayName { get; init; } = string.Empty;

        public string RankText { get; init; } = string.Empty;

        public string RecentRecordText { get; init; } = string.Empty;

        public string KdaText { get; init; } = string.Empty;

        public string StatusText { get; init; } = string.Empty;

        public IReadOnlyList<LcuCompanionRecentResultViewModel> RecentResults
        {
            get;
            init;
        } = [];

        public bool IsLoading { get; init; }

        public bool IsUnavailable { get; init; }
    }

    public sealed class LcuCompanionAutomationCardViewModel : BindableBase
    {
        private string _championName = "--";
        private string _championIcon = string.Empty;

        public string Label { get; init; } = string.Empty;

        public int ChampionId { get; init; }

        public string StatusText { get; init; } = string.Empty;

        public bool IsEnabled { get; init; }

        public string ChampionName
        {
            get => _championName;
            set => SetProperty(ref _championName, value);
        }

        public string ChampionIcon
        {
            get => _championIcon;
            set => SetProperty(ref _championIcon, value);
        }
    }

    public sealed class LcuCompanionViewModel : BindableBase
    {
        private readonly IEventAggregator _eventAggregator;
        private readonly IMatchService _matchService;
        private readonly IGameAutomationSettings _automationSettings;
        private readonly IGameResourceManager _gameResourceManager;
        private readonly IResourceService _resourceService;
        private readonly LatestValueDispatcher<LiveMatchSnapshot> _snapshotDispatcher;
        private Task<IReadOnlyDictionary<int, string>> _championNamesTask;
        private LiveMatchSnapshot _snapshot = LiveMatchSnapshot.Empty;
        private long _resourceGeneration;
        private bool _started;

        public LcuCompanionViewModel(
            IEventAggregator eventAggregator,
            IMatchService matchService,
            IGameAutomationSettings automationSettings,
            IGameResourceManager gameResourceManager,
            IResourceService resourceService)
        {
            _eventAggregator = eventAggregator ??
                throw new ArgumentNullException(nameof(eventAggregator));
            _matchService = matchService ?? throw new ArgumentNullException(nameof(matchService));
            _automationSettings = automationSettings ??
                throw new ArgumentNullException(nameof(automationSettings));
            _gameResourceManager = gameResourceManager ??
                throw new ArgumentNullException(nameof(gameResourceManager));
            _resourceService = resourceService ??
                throw new ArgumentNullException(nameof(resourceService));
            _snapshotDispatcher = new LatestValueDispatcher<LiveMatchSnapshot>(
                action => Dispatch(action, DispatcherPriority.Background),
                ApplySnapshot);

            Teammates = [];
            AutomationCards = [];
        }

        public ObservableCollection<LcuCompanionPlayerViewModel> Teammates { get; }

        public ObservableCollection<LcuCompanionAutomationCardViewModel> AutomationCards { get; }

        private string _modeText = string.Empty;
        public string ModeText
        {
            get => _modeText;
            private set => SetProperty(ref _modeText, value);
        }

        private string _teamStatusText = string.Empty;
        public string TeamStatusText
        {
            get => _teamStatusText;
            private set => SetProperty(ref _teamStatusText, value);
        }

        public void Start()
        {
            if (_started)
            {
                return;
            }

            _started = true;
            _matchService.SnapshotChanged += HandleSnapshotChanged;
            _automationSettings.Changed += HandleAutomationSettingsChanged;
            _eventAggregator.GetEvent<LanguageSwitchedEvent>()
                .Subscribe(HandleLanguageSwitched);
            ApplySnapshot(_matchService.Current ?? LiveMatchSnapshot.Empty);
        }

        public void Stop()
        {
            if (!_started)
            {
                return;
            }

            _started = false;
            _resourceGeneration++;
            _matchService.SnapshotChanged -= HandleSnapshotChanged;
            _automationSettings.Changed -= HandleAutomationSettingsChanged;
            _eventAggregator.GetEvent<LanguageSwitchedEvent>()
                .Unsubscribe(HandleLanguageSwitched);
        }

        private void HandleSnapshotChanged(
            object sender,
            LiveMatchSnapshotChangedEventArgs args)
        {
            _snapshotDispatcher.Publish(args?.Snapshot ?? LiveMatchSnapshot.Empty);
        }

        private void HandleAutomationSettingsChanged(object sender, EventArgs args)
        {
            Dispatch(() => ApplySnapshot(_snapshot));
        }

        private void HandleLanguageSwitched()
        {
            Dispatch(() => ApplySnapshot(_snapshot));
        }

        private void ApplySnapshot(LiveMatchSnapshot snapshot)
        {
            if (!_started)
            {
                return;
            }

            _snapshot = snapshot ?? LiveMatchSnapshot.Empty;
            ModeText = GetModeText(LcuCompanionPresentation.GetMode(_snapshot));

            var localCellId = _snapshot.ChampionSelect?.LocalPlayerCellId ?? 0;
            var teammates = (_snapshot.Roster?.MyTeam ??
                    Array.Empty<LiveMatchPlayerSnapshot>())
                .Where(player => player is not null &&
                    !player.IsLocalPlayer &&
                    (localCellId <= 0 || player.CellId != localCellId))
                .Take(4)
                .Select(CreatePlayer)
                .ToList();
            while (teammates.Count < 4)
            {
                teammates.Add(CreateLoadingPlayer(teammates.Count + 1));
            }

            Replace(Teammates, teammates);
            TeamStatusText = GetTeamStatusText(teammates);

            var cards = CreateAutomationCards(_snapshot);
            Replace(AutomationCards, cards);
            var generation = ++_resourceGeneration;
            _ = LoadChampionResourcesAsync(cards, generation);
        }

        private LcuCompanionPlayerViewModel CreatePlayer(LiveMatchPlayerSnapshot player)
        {
            var isHidden = player.IsHidden ||
                player.DataState == LiveMatchPlayerDataState.Hidden;
            var isLoaded = player.DataState == LiveMatchPlayerDataState.Loaded;
            var recentCount = Math.Max(0, player.RecentMatchCount);
            var winRate = recentCount == 0
                ? 0
                : (int)Math.Round(player.RecentWins * 100d / recentCount,
                    MidpointRounding.AwayFromZero);
            return new LcuCompanionPlayerViewModel
            {
                ChampionIcon = player.ChampionIcon ?? string.Empty,
                DisplayName = isHidden
                    ? Text("Match.Live.Player.Hidden", "Hidden player")
                    : FormatDisplayName(player),
                RankText = isLoaded ? FormatRank(player.SoloRank) : "--",
                RecentRecordText = isLoaded
                    ? string.Format(Text("Match.Live.Record.Format", "{0}W {1}L · {2}%"),
                        player.RecentWins, player.RecentLosses, winRate)
                    : "--",
                KdaText = isLoaded
                    ? string.Format(Text("Match.Live.Kda.Format", "KDA {0:0.0}"),
                        player.AverageKda)
                    : "KDA --",
                StatusText = GetPlayerStatusText(player, isHidden, isLoaded, recentCount),
                RecentResults = isLoaded ? CreateRecentResults(player) : [],
                IsLoading = player.DataState == LiveMatchPlayerDataState.Loading,
                IsUnavailable = isHidden ||
                    player.DataState is LiveMatchPlayerDataState.Error or
                        LiveMatchPlayerDataState.Unavailable
            };
        }

        private static IReadOnlyList<LcuCompanionRecentResultViewModel>
            CreateRecentResults(LiveMatchPlayerSnapshot player)
        {
            var results = player.RecentResults ?? Array.Empty<bool>();
            if (results.Count == 0)
            {
                results = (player.RecentMatches ??
                        Array.Empty<LiveMatchRecentMatchSnapshot>())
                    .Select(match => match.IsWin)
                    .ToArray();
            }

            return results
                .Take(20)
                .Select(isWin => new LcuCompanionRecentResultViewModel
                {
                    IsWin = isWin
                })
                .ToArray();
        }

        private LcuCompanionPlayerViewModel CreateLoadingPlayer(int slot)
        {
            return new LcuCompanionPlayerViewModel
            {
                DisplayName = string.Format(
                    Text("Companion.Team.Slot", "Teammate {0}"), slot),
                RankText = "--",
                RecentRecordText = "--",
                KdaText = "KDA --",
                StatusText = Text("Match.Live.Player.Loading", "Loading player data"),
                IsLoading = true
            };
        }

        private LcuCompanionAutomationCardViewModel[] CreateAutomationCards(
            LiveMatchSnapshot snapshot)
        {
            var mode = LcuCompanionPresentation.GetMode(snapshot);
            if (mode is LcuCompanionMode.Aram or LcuCompanionMode.HextechAram)
            {
                var currentChampionId =
                    LcuCompanionPresentation.GetLocalChampionId(snapshot);
                var targetChampionId = _automationSettings.AutoSwapAramBench
                    ? LcuCompanionPresentation.GetAramAutomationTarget(
                        snapshot, _automationSettings.PreferredAramChampionIds)
                    : 0;
                return
                [
                    CreateCard(
                        Text("Companion.Automation.Current", "Current champion"),
                        currentChampionId,
                        currentChampionId > 0
                            ? Text("Companion.Status.Selected", "Selected")
                            : Text("Companion.Status.Waiting", "Waiting"),
                        currentChampionId > 0),
                    CreateCard(
                        Text("Companion.Automation.Aram", "Auto swap"),
                        targetChampionId,
                        GetAramStatus(snapshot, currentChampionId, targetChampionId),
                        _automationSettings.AutoSwapAramBench)
                ];
            }

            return
            [
                CreateChampionSelectCard(snapshot, "ban",
                    Text("Companion.Automation.Ban", "Auto Ban"),
                    _automationSettings.AutoBanChampion,
                    _automationSettings.PreferredBanChampionIds),
                CreateChampionSelectCard(snapshot, "pick",
                    Text("Companion.Automation.Pick", "Auto Pick"),
                    _automationSettings.AutoPickChampion,
                    _automationSettings.PreferredPickChampionIds)
            ];
        }

        private LcuCompanionAutomationCardViewModel CreateChampionSelectCard(
            LiveMatchSnapshot snapshot,
            string actionType,
            string label,
            bool enabled,
            IReadOnlyList<int> preferredChampionIds)
        {
            var championId = enabled
                ? LcuCompanionPresentation.GetChampionSelectAutomationTarget(
                    snapshot, actionType, preferredChampionIds)
                : 0;
            var action = LcuCompanionPresentation.FindLocalAction(snapshot, actionType);
            var status = !enabled
                ? Text("Companion.Status.Disabled", "Disabled")
                : championId <= 0
                    ? Text("Companion.Status.NotConfigured", "Not configured")
                    : action?.Completed == true
                        ? Text("Companion.Status.Completed", "Completed")
                        : action?.IsInProgress == true
                            ? Text("Companion.Status.Executing", "Executing")
                            : Text("Companion.Status.Waiting", "Waiting");
            return CreateCard(label, championId, status, enabled);
        }

        private string GetAramStatus(
            LiveMatchSnapshot snapshot,
            int currentChampionId,
            int targetChampionId)
        {
            if (!_automationSettings.AutoSwapAramBench)
            {
                return Text("Companion.Status.Disabled", "Disabled");
            }

            if ((_automationSettings.PreferredAramChampionIds?.Count ?? 0) == 0)
            {
                return Text("Companion.Status.NotConfigured", "Not configured");
            }

            if (targetChampionId <= 0)
            {
                return snapshot?.ChampionSelect?.BenchEnabled == true
                    ? Text("Companion.Status.NoCandidate", "No candidate on bench")
                    : Text("Companion.Status.BenchUnavailable", "Bench unavailable");
            }

            return targetChampionId == currentChampionId
                ? Text("Companion.Status.Completed", "Completed")
                : Text("Companion.Status.Executing", "Executing");
        }

        private static LcuCompanionAutomationCardViewModel CreateCard(
            string label,
            int championId,
            string status,
            bool enabled)
        {
            return new LcuCompanionAutomationCardViewModel
            {
                Label = label,
                ChampionId = championId,
                ChampionName = championId > 0 ? $"#{championId}" : "--",
                StatusText = status,
                IsEnabled = enabled
            };
        }

        private async Task LoadChampionResourcesAsync(
            IReadOnlyCollection<LcuCompanionAutomationCardViewModel> cards,
            long generation)
        {
            var championCards = cards.Where(card => card.ChampionId > 0).ToArray();
            if (championCards.Length == 0)
            {
                return;
            }

            try
            {
                _championNamesTask ??= LoadChampionNamesAsync();
                var names = await _championNamesTask.ConfigureAwait(false);
                var resources = await Task.WhenAll(championCards.Select(async card =>
                {
                    string icon = string.Empty;
                    try
                    {
                        icon = await _gameResourceManager
                            .GetChampoinIconByIdAsync(card.ChampionId)
                            .ConfigureAwait(false) ?? string.Empty;
                    }
                    catch (Exception exception)
                    {
                        Log.Debug(exception,
                            "Unable to load companion champion icon {ChampionId}",
                            card.ChampionId);
                    }

                    return (Card: card,
                        Name: names.TryGetValue(card.ChampionId, out var name)
                            ? name
                            : $"#{card.ChampionId}",
                        Icon: icon);
                })).ConfigureAwait(false);

                Dispatch(() =>
                {
                    if (!_started || generation != _resourceGeneration)
                    {
                        return;
                    }

                    foreach (var resource in resources)
                    {
                        resource.Card.ChampionName = resource.Name;
                        resource.Card.ChampionIcon = resource.Icon;
                    }
                });
            }
            catch (Exception exception)
            {
                Log.Debug(exception, "Unable to load companion champion resources");
            }
        }

        private async Task<IReadOnlyDictionary<int, string>> LoadChampionNamesAsync()
        {
            var champions = await _gameResourceManager.GetChampionSummarysAsync()
                .ConfigureAwait(false) ?? [];
            return champions
                .Where(champion => champion is not null && champion.Id > 0)
                .GroupBy(champion => champion.Id)
                .ToDictionary(
                    group => group.Key,
                    group => group.First().Name ?? $"#{group.Key}");
        }

        private string GetModeText(LcuCompanionMode mode)
        {
            return mode switch
            {
                LcuCompanionMode.RankedSoloDuo =>
                    Text("Companion.Mode.RankedSoloDuo", "Ranked Solo/Duo"),
                LcuCompanionMode.RankedFlex =>
                    Text("Companion.Mode.RankedFlex", "Ranked Flex"),
                LcuCompanionMode.Aram => Text("Companion.Mode.Aram", "ARAM"),
                LcuCompanionMode.HextechAram =>
                    Text("Companion.Mode.HextechAram", "Hextech ARAM"),
                _ => Text("Companion.Mode.Matchmade", "Matchmade")
            };
        }

        private string GetTeamStatusText(
            IReadOnlyCollection<LcuCompanionPlayerViewModel> teammates)
        {
            if (teammates.Count == 0)
            {
                return Text("Companion.Team.Loading", "Loading teammates");
            }

            var available = teammates.Count(player => !player.IsUnavailable && !player.IsLoading);
            return string.Format(
                Text("Companion.Team.Available", "Available {0}/{1}"),
                available,
                teammates.Count);
        }

        private string GetPlayerStatusText(
            LiveMatchPlayerSnapshot player,
            bool isHidden,
            bool isLoaded,
            int recentCount)
        {
            if (isHidden)
            {
                return Text("Match.Live.Player.Hidden.Description",
                    "Identity hidden by the client");
            }

            return player.DataState switch
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
                var gameName = FirstNotEmpty(
                    summoner.GameName,
                    summoner.DisplayName,
                    summoner.SummonerName);
                if (!string.IsNullOrWhiteSpace(gameName))
                {
                    return string.IsNullOrWhiteSpace(summoner.TagLine)
                        ? gameName
                        : $"{gameName}#{summoner.TagLine}";
                }
            }

            return !string.IsNullOrWhiteSpace(player.DisplayName)
                ? player.DisplayName
                : Text("Match.Live.Player.Unknown", "Unknown player");
        }

        private string FormatRank(Rank rank)
        {
            if (rank is null || rank.Tier == Tier.UNRANKED)
            {
                return Text("Match.Live.Rank.Unranked", "Unranked");
            }

            var key = rank.Tier switch
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
            var tier = Text(key, rank.Tier.ToString());
            var division = string.IsNullOrWhiteSpace(rank.Division) ||
                string.Equals(rank.Division, nameof(Division.NA),
                    StringComparison.OrdinalIgnoreCase)
                ? string.Empty
                : $" {rank.Division}";
            return $"{tier}{division} · {rank.LeaguePoints} LP";
        }

        private string Text(string key, string fallback)
        {
            try
            {
                return _resourceService.FindResource<string>(key) ?? fallback;
            }
            catch (Exception exception)
            {
                Log.Debug(exception,
                    "Unable to resolve companion resource {ResourceKey}", key);
                return fallback;
            }
        }

        private static string FirstNotEmpty(params string[] values)
        {
            return values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ??
                string.Empty;
        }

        private static void Replace<T>(
            ObservableCollection<T> target,
            IEnumerable<T> values)
        {
            target.Clear();
            foreach (var value in values)
            {
                target.Add(value);
            }
        }

        private static void Dispatch(
            Action action,
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

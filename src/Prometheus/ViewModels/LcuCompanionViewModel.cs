using Prism.Commands;
using Prism.Events;
using Prism.Mvvm;
using Prometheus.Core.Events;
using Prometheus.Core.Models;
using Prometheus.Core.Mvvm;
using Prometheus.Core.Presentation;
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

        public bool HasToggle => ToggleCommand is not null;

        public DelegateCommand ToggleCommand { get; init; }

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

    public sealed class LcuCompanionRunePerkViewModel
    {
        public int PerkId { get; init; }

        public string Name { get; init; } = string.Empty;

        public string Icon { get; init; } = string.Empty;
    }

    public sealed partial class LcuCompanionViewModel : BindableBase
    {
        private const int ChampionNameLoadAttempts = 4;
        private const int ChampionNameRetryDelayMilliseconds = 150;

        private readonly IEventAggregator _eventAggregator;
        private readonly IMatchService _matchService;
        private readonly IGameService _gameService;
        private readonly IGameAutomationSettings _automationSettings;
        private readonly IGameResourceManager _gameResourceManager;
        private readonly IResourceService _resourceService;
        private readonly LatestValueDispatcher<LiveMatchSnapshot> _snapshotDispatcher;
        private Task<IReadOnlyDictionary<int, string>> _championNamesTask;
        private readonly Dictionary<int, (string Name, string Icon)> _championResources = [];
        private string _championResourceRequestKey = string.Empty;
        private LiveMatchSnapshot _snapshot = LiveMatchSnapshot.Empty;
        private long _resourceGeneration;
        private long _runeGeneration;
        private CancellationTokenSource _runeRecommendationCts;
        private CancellationTokenSource _runeApplyCts;
        private RuneRecommendationSet _runeRecommendations;
        private RuneRecommendationOption _selectedRuneRecommendation;
        private IReadOnlyDictionary<int, (string Name, string Icon)> _runeResources =
            new Dictionary<int, (string Name, string Icon)>();
        private RuneRecommendationKind _selectedRuneKind = RuneRecommendationKind.Popular;
        private string _runeRequestKey = string.Empty;
        private string _runeChampionName = string.Empty;
        private bool _isRuneChampionNameResolved;
        private string _appliedRuneSignature = string.Empty;
        private bool _started;

        public LcuCompanionViewModel(
            IEventAggregator eventAggregator,
            IMatchService matchService,
            IGameService gameService,
            IGameAutomationSettings automationSettings,
            IGameResourceManager gameResourceManager,
            IResourceService resourceService)
        {
            _eventAggregator = eventAggregator ??
                throw new ArgumentNullException(nameof(eventAggregator));
            _matchService = matchService ?? throw new ArgumentNullException(nameof(matchService));
            _gameService = gameService ?? throw new ArgumentNullException(nameof(gameService));
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
            RunePerks = [];
            SelectPopularRuneCommand = new DelegateCommand(
                () => SelectRuneRecommendation(RuneRecommendationKind.Popular));
            SelectWinRateRuneCommand = new DelegateCommand(
                () => SelectRuneRecommendation(RuneRecommendationKind.WinRate));
            ApplyRuneCommand = new DelegateCommand(
                ExecuteApplyRune,
                CanApplyRune);
        }

        public ObservableCollection<LcuCompanionPlayerViewModel> Teammates { get; }

        public ObservableCollection<LcuCompanionAutomationCardViewModel> AutomationCards { get; }

        public ObservableCollection<LcuCompanionRunePerkViewModel> RunePerks { get; }

        public DelegateCommand SelectPopularRuneCommand { get; }

        public DelegateCommand SelectWinRateRuneCommand { get; }

        public DelegateCommand ApplyRuneCommand { get; }

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

        private bool _isRuneRecommendationVisible;
        public bool IsRuneRecommendationVisible
        {
            get => _isRuneRecommendationVisible;
            private set => SetProperty(ref _isRuneRecommendationVisible, value);
        }

        private bool _isRuneRecommendationLoading;
        public bool IsRuneRecommendationLoading
        {
            get => _isRuneRecommendationLoading;
            private set => SetProperty(ref _isRuneRecommendationLoading, value);
        }

        private bool _hasRuneRecommendation;
        public bool HasRuneRecommendation
        {
            get => _hasRuneRecommendation;
            private set => SetProperty(ref _hasRuneRecommendation, value);
        }

        private bool _isPopularRuneSelected = true;
        public bool IsPopularRuneSelected
        {
            get => _isPopularRuneSelected;
            private set => SetProperty(ref _isPopularRuneSelected, value);
        }

        private bool _isWinRateRuneSelected;
        public bool IsWinRateRuneSelected
        {
            get => _isWinRateRuneSelected;
            private set => SetProperty(ref _isWinRateRuneSelected, value);
        }

        private bool _isRuneRecommendationValid;
        public bool IsRuneRecommendationValid
        {
            get => _isRuneRecommendationValid;
            private set
            {
                if (SetProperty(ref _isRuneRecommendationValid, value))
                {
                    ApplyRuneCommand.RaiseCanExecuteChanged();
                }
            }
        }

        private bool _isApplyingRune;
        public bool IsApplyingRune
        {
            get => _isApplyingRune;
            private set
            {
                if (SetProperty(ref _isApplyingRune, value))
                {
                    ApplyRuneCommand.RaiseCanExecuteChanged();
                }
            }
        }

        private string _runeChampionText = string.Empty;
        public string RuneChampionText
        {
            get => _runeChampionText;
            private set => SetProperty(ref _runeChampionText, value);
        }

        private string _runeStyleText = string.Empty;
        public string RuneStyleText
        {
            get => _runeStyleText;
            private set => SetProperty(ref _runeStyleText, value);
        }

        private string _runeStatsText = string.Empty;
        public string RuneStatsText
        {
            get => _runeStatsText;
            private set => SetProperty(ref _runeStatsText, value);
        }

        private string _runeSourceText = string.Empty;
        public string RuneSourceText
        {
            get => _runeSourceText;
            private set => SetProperty(ref _runeSourceText, value);
        }

        private string _runeStatusText = string.Empty;
        public string RuneStatusText
        {
            get => _runeStatusText;
            private set => SetProperty(ref _runeStatusText, value);
        }

        private string _runeApplyButtonText = string.Empty;
        public string RuneApplyButtonText
        {
            get => _runeApplyButtonText;
            private set => SetProperty(ref _runeApplyButtonText, value);
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
            _championResourceRequestKey = string.Empty;
            _championResources.Clear();
            Interlocked.Exchange(ref _championNamesTask, null);
            _runeGeneration++;
            CancelRuneOperations();
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
            Dispatch(() =>
            {
                _resourceGeneration++;
                _championResourceRequestKey = string.Empty;
                _championResources.Clear();
                Interlocked.Exchange(ref _championNamesTask, null);
                ApplySnapshot(_snapshot);
            });
        }

        private void ApplySnapshot(LiveMatchSnapshot snapshot)
        {
            if (!_started)
            {
                return;
            }

            _snapshot = snapshot ?? LiveMatchSnapshot.Empty;
            var mode = LcuCompanionPresentation.GetMode(_snapshot);
            ModeText = GetModeText(mode);

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

            var cards = CreateAutomationCards(_snapshot, mode);
            Replace(AutomationCards, cards);
            ApplyCachedChampionResources(cards);
            var championIds = cards
                .Where(card => card.ChampionId > 0)
                .Select(card => card.ChampionId)
                .Distinct()
                .ToArray();
            var resourceKey = GetChampionResourceRequestKey(championIds);
            if (!string.Equals(resourceKey, _championResourceRequestKey,
                    StringComparison.Ordinal))
            {
                _championResourceRequestKey = resourceKey;
                var generation = ++_resourceGeneration;
                _ = LoadChampionResourcesAsync(
                    championIds.Where(championId =>
                        !_championResources.ContainsKey(championId))
                        .ToArray(),
                    generation);
            }
            UpdateRuneRecommendation(_snapshot, mode);
            ApplyRuneCommand.RaiseCanExecuteChanged();
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
                    : LiveMatchPlayerTextFormatter.FormatDisplayName(
                        player,
                        () => Text("Match.Live.Player.Unknown", "Unknown player"),
                        () => Text("Match.Live.Player.Unknown", "Unknown player")),
                RankText = isLoaded
                    ? LiveMatchPlayerTextFormatter.FormatRank(player.SoloRank, Text)
                    : "--",
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

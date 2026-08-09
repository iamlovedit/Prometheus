using Prism.Events;
using Prism.Commands;
using Prism.Mvvm;
using Prometheus.Core.Events;
using Prometheus.Core.Logging;
using Prometheus.Core.Models;
using Prometheus.Core.Mvvm;
using Prometheus.Core.Tasks;
using Prometheus.Desktop.Services;
using Prometheus.Services.Interfaces.Client;
using Serilog;
using Serilog.Events;
using System.Collections.ObjectModel;
using System.Diagnostics;
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

    public sealed class LcuCompanionRunePerkViewModel
    {
        public int PerkId { get; init; }

        public string Name { get; init; } = string.Empty;

        public string Icon { get; init; } = string.Empty;
    }

    public sealed class LcuCompanionViewModel : BindableBase
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
            UpdateRuneRecommendation(_snapshot);
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
                var names = await GetChampionNamesAsync().ConfigureAwait(false);
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
                .Where(champion => champion is not null &&
                    champion.Id > 0 &&
                    !string.IsNullOrWhiteSpace(champion.Name))
                .GroupBy(champion => champion.Id)
                .ToDictionary(
                    group => group.Key,
                    group => group.First().Name.Trim());
        }

        private async Task<IReadOnlyDictionary<int, string>> GetChampionNamesAsync(
            int requiredChampionId = 0)
        {
            var task = Volatile.Read(ref _championNamesTask);
            if (task is null)
            {
                var createdTask = LoadChampionNamesAsync();
                task = Interlocked.CompareExchange(
                    ref _championNamesTask,
                    createdTask,
                    null) ?? createdTask;
            }

            try
            {
                var names = await task.ConfigureAwait(false);
                if (names.Count == 0 ||
                    (requiredChampionId > 0 && !names.ContainsKey(requiredChampionId)))
                {
                    _ = Interlocked.CompareExchange(ref _championNamesTask, null, task);
                }

                return names;
            }
            catch
            {
                _ = Interlocked.CompareExchange(ref _championNamesTask, null, task);
                throw;
            }
        }

        private async Task<string> ResolveChampionNameAsync(
            int championId,
            CancellationToken cancellationToken)
        {
            for (var attempt = 0; attempt < ChampionNameLoadAttempts; attempt++)
            {
                if (attempt > 0)
                {
                    await Task.Delay(
                            ChampionNameRetryDelayMilliseconds * attempt,
                            cancellationToken)
                        .ConfigureAwait(false);
                }

                try
                {
                    var names = await GetChampionNamesAsync(championId)
                        .ConfigureAwait(false);
                    if (names.TryGetValue(championId, out var name) &&
                        IsResolvedChampionName(name, championId))
                    {
                        return name;
                    }
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    Log.Debug(exception,
                        "Unable to resolve champion name {ChampionId} on attempt {Attempt}",
                        championId,
                        attempt + 1);
                }
            }

            return null;
        }

        private static bool IsResolvedChampionName(string name, int championId)
        {
            return !string.IsNullOrWhiteSpace(name) &&
                !string.Equals(name, $"#{championId}", StringComparison.Ordinal);
        }

        private void UpdateRuneRecommendation(LiveMatchSnapshot snapshot)
        {
            var mode = LcuCompanionPresentation.GetMode(snapshot);
            var shouldShow = snapshot?.GameflowPhase == GameflowPhase.ChampSelect &&
                mode != LcuCompanionMode.HextechAram;
            IsRuneRecommendationVisible = shouldShow;
            if (!shouldShow)
            {
                ResetRuneRecommendation(hide: true);
                return;
            }

            var championId = LcuCompanionPresentation.GetLocalChampionId(snapshot);
            var lane = LcuCompanionPresentation.GetLocalAssignedPosition(snapshot);
            var isAram = mode == LcuCompanionMode.Aram;
            var requestKey = championId > 0
                ? $"{championId}:{lane}:{isAram}"
                : string.Empty;
            if (championId <= 0)
            {
                if (!string.IsNullOrEmpty(_runeRequestKey))
                {
                    ResetRuneRecommendation(hide: false);
                }

                RuneStatusText = Text(
                    "Companion.Runes.WaitingForChampion",
                    "Select a champion to view recommendations");
                RuneApplyButtonText = Text(
                    "Companion.Runes.Apply",
                    "Apply to League Client");
                return;
            }

            if (string.Equals(requestKey, _runeRequestKey, StringComparison.Ordinal))
            {
                if (IsRuneRecommendationLoading)
                {
                    RuneStatusText = Text(
                        "Companion.Runes.Loading",
                        "Loading rune recommendations");
                }
                else
                {
                    RefreshRunePresentation();
                }

                return;
            }

            CancelRuneOperations();
            _runeRequestKey = requestKey;
            _runeRecommendations = null;
            _selectedRuneRecommendation = null;
            _runeResources = new Dictionary<int, (string Name, string Icon)>();
            _runeChampionName = $"#{championId}";
            _isRuneChampionNameResolved = false;
            _selectedRuneKind = RuneRecommendationKind.Popular;
            _appliedRuneSignature = string.Empty;
            Replace(RunePerks, []);
            HasRuneRecommendation = false;
            IsRuneRecommendationValid = false;
            IsRuneRecommendationLoading = true;
            IsPopularRuneSelected = true;
            IsWinRateRuneSelected = false;
            RuneChampionText = $"#{championId}";
            RuneStyleText = string.Empty;
            RuneStatsText = string.Empty;
            RuneSourceText = string.Empty;
            RuneStatusText = Text(
                "Companion.Runes.Loading",
                "Loading rune recommendations");
            RuneApplyButtonText = Text(
                "Companion.Runes.Apply",
                "Apply to League Client");

            var generation = ++_runeGeneration;
            _runeRecommendationCts = new CancellationTokenSource();
            LoadRuneRecommendationAsync(
                    championId,
                    lane,
                    isAram,
                    requestKey,
                    generation,
                    _runeRecommendationCts.Token)
                .Observe("Loading companion rune recommendations");
        }

        private async Task LoadRuneRecommendationAsync(
            int championId,
            string lane,
            bool isAram,
            string requestKey,
            long generation,
            CancellationToken cancellationToken)
        {
            RuneRecommendationSet recommendations;
            try
            {
                recommendations = await _gameService.GetRuneRecommendationsAsync(
                        championId, lane, isAram, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception exception)
            {
                Log.Warning(exception,
                    "Unable to load companion rune recommendations for champion {ChampionId}",
                    championId);
                Dispatch(() => CompleteRuneLoadFailure(requestKey, generation));
                return;
            }

            if (recommendations is null)
            {
                Dispatch(() => CompleteRuneLoadFailure(requestKey, generation));
                return;
            }

            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                var perks = await _gameResourceManager.GetPerksAsync()
                    .ConfigureAwait(false) ?? [];
                var metadata = perks
                    .Where(perk => perk is not null && perk.Id > 0)
                    .GroupBy(perk => perk.Id)
                    .ToDictionary(group => group.Key, group => group.First());
                var allPerkIds = (recommendations.Popular?.SelectedPerkIds ?? [])
                    .Concat(recommendations.WinRate?.SelectedPerkIds ?? [])
                    .Where(perkId => perkId > 0)
                    .Distinct()
                    .ToArray();
                var resources = new Dictionary<int, (string Name, string Icon)>();
                foreach (var perkId in allPerkIds)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (!metadata.TryGetValue(perkId, out var perk))
                    {
                        continue;
                    }

                    var icon = await _gameResourceManager.GetPerkIconByIdAsync(perkId)
                        .ConfigureAwait(false) ?? string.Empty;
                    resources[perkId] = (
                        string.IsNullOrWhiteSpace(perk.Name) ? $"#{perkId}" : perk.Name,
                        icon);
                }

                var championName = await ResolveChampionNameAsync(
                        championId,
                        cancellationToken)
                    .ConfigureAwait(false);
                var isChampionNameResolved = IsResolvedChampionName(
                    championName,
                    championId);
                Dispatch(() =>
                {
                    if (!CanCommitRuneResult(requestKey, generation))
                    {
                        return;
                    }

                    _runeRecommendations = recommendations;
                    _runeResources = resources;
                    _runeChampionName = isChampionNameResolved
                        ? championName
                        : $"#{championId}";
                    _isRuneChampionNameResolved = isChampionNameResolved;
                    IsRuneRecommendationLoading = false;
                    RefreshRunePresentation();
                });
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception exception)
            {
                Log.Warning(exception,
                    "Unable to resolve companion rune resources for champion {ChampionId}",
                    championId);
                Dispatch(() => CompleteRuneLoadFailure(requestKey, generation));
            }
        }

        private void CompleteRuneLoadFailure(string requestKey, long generation)
        {
            if (!CanCommitRuneResult(requestKey, generation))
            {
                return;
            }

            IsRuneRecommendationLoading = false;
            HasRuneRecommendation = false;
            IsRuneRecommendationValid = false;
            RuneStatusText = Text(
                "Companion.Runes.Unavailable",
                "Rune recommendations are unavailable");
        }

        private bool CanCommitRuneResult(string requestKey, long generation)
        {
            return _started &&
                generation == _runeGeneration &&
                string.Equals(requestKey, _runeRequestKey, StringComparison.Ordinal);
        }

        private void SelectRuneRecommendation(RuneRecommendationKind kind)
        {
            if (_runeRecommendations is null || IsRuneRecommendationLoading)
            {
                return;
            }

            _selectedRuneKind = kind;
            RefreshRunePresentation();
        }

        private void RefreshRunePresentation()
        {
            if (_runeRecommendations is null)
            {
                return;
            }

            _selectedRuneRecommendation = _selectedRuneKind == RuneRecommendationKind.WinRate
                ? _runeRecommendations.WinRate ?? _runeRecommendations.Popular
                : _runeRecommendations.Popular ?? _runeRecommendations.WinRate;
            if (_selectedRuneRecommendation is null)
            {
                HasRuneRecommendation = false;
                IsRuneRecommendationValid = false;
                RuneStatusText = Text(
                    "Companion.Runes.Unavailable",
                    "Rune recommendations are unavailable");
                return;
            }

            IsPopularRuneSelected = _selectedRuneKind == RuneRecommendationKind.Popular;
            IsWinRateRuneSelected = _selectedRuneKind == RuneRecommendationKind.WinRate;
            HasRuneRecommendation = true;
            var laneText = GetRuneLaneText(_runeRecommendations.Lane);
            RuneChampionText = string.IsNullOrWhiteSpace(laneText)
                ? _runeChampionName
                : $"{_runeChampionName} · {laneText}";
            RuneStyleText = string.Format(
                Text("Companion.Runes.Style.Format", "{0} > {1}"),
                GetRuneStyleText(_selectedRuneRecommendation.PrimaryStyleId),
                GetRuneStyleText(_selectedRuneRecommendation.SubStyleId));
            RuneStatsText = FormatRuneStats(_selectedRuneRecommendation);
            RuneSourceText = string.IsNullOrWhiteSpace(_runeRecommendations.DataVersion)
                ? _runeRecommendations.Source
                : $"{_runeRecommendations.Source} · {_runeRecommendations.DataVersion}";

            var perkViewModels = _selectedRuneRecommendation.SelectedPerkIds
                .Select(perkId => _runeResources.TryGetValue(perkId, out var resource)
                    ? new LcuCompanionRunePerkViewModel
                    {
                        PerkId = perkId,
                        Name = resource.Name,
                        Icon = resource.Icon
                    }
                    : new LcuCompanionRunePerkViewModel
                    {
                        PerkId = perkId,
                        Name = $"#{perkId}"
                    })
                .ToArray();
            Replace(RunePerks, perkViewModels);
            IsRuneRecommendationValid = perkViewModels.Length == 9 &&
                perkViewModels.All(perk => _runeResources.ContainsKey(perk.PerkId));
            var isApplied = string.Equals(
                _appliedRuneSignature,
                GetRuneSignature(_selectedRuneRecommendation),
                StringComparison.Ordinal);
            RuneStatusText = !IsRuneRecommendationValid
                ? Text("Companion.Runes.Outdated", "Recommendation does not match this client version")
                : !_isRuneChampionNameResolved
                    ? Text("Companion.Runes.ChampionUnavailable", "Unable to resolve champion name")
                : isApplied
                    ? Text("Companion.Runes.Applied", "Rune page applied")
                    : Text("Companion.Runes.Ready", "Ready to apply");
            RuneApplyButtonText = isApplied
                ? Text("Companion.Runes.Applied.Button", "Applied")
                : Text("Companion.Runes.Apply", "Apply to League Client");
            ApplyRuneCommand.RaiseCanExecuteChanged();
        }

        private void ExecuteApplyRune()
        {
            ApplyRuneRecommendationAsync().Observe("Applying companion rune recommendation");
        }

        private bool CanApplyRune()
        {
            return _started &&
                _snapshot.GameflowPhase == GameflowPhase.ChampSelect &&
                _snapshot.ConnectionState == ConnectionState.Connected &&
                IsRuneRecommendationVisible &&
                IsRuneRecommendationValid &&
                _isRuneChampionNameResolved &&
                !IsRuneRecommendationLoading &&
                !IsApplyingRune &&
                _selectedRuneRecommendation is not null;
        }

        private async Task ApplyRuneRecommendationAsync()
        {
            if (!CanApplyRune())
            {
                return;
            }

            var recommendation = _selectedRuneRecommendation;
            var championId = _runeRecommendations?.ChampionId ?? 0;
            var queueId = LcuCompanionPresentation.GetQueueId(_snapshot);
            var requestKey = _runeRequestKey;
            var operationId = Guid.NewGuid();
            var stopwatch = Stopwatch.StartNew();
            _runeApplyCts?.Cancel();
            _runeApplyCts?.Dispose();
            var cancellationTokenSource = new CancellationTokenSource();
            _runeApplyCts = cancellationTokenSource;
            IsApplyingRune = true;
            RuneStatusText = Text("Companion.Runes.Applying", "Applying rune page");
            RuneApplyButtonText = Text("Companion.Runes.Applying.Button", "Applying...");

            try
            {
                var result = await _gameService.ApplyRuneRecommendationAsync(
                    GetManagedRunePageName(),
                    recommendation,
                    cancellationTokenSource.Token);
                stopwatch.Stop();
                if (result.PageCreated)
                {
                    WriteRuneOperation(
                        LogEventLevel.Information,
                        "rune.page.create",
                        "Succeeded",
                        operationId,
                        championId,
                        queueId,
                        result.RunePageId,
                        stopwatch.ElapsedMilliseconds,
                        Text("Companion.Runes.Log.Created", "Created managed rune page"));
                }

                if (result.Succeeded)
                {
                    if (string.Equals(_runeRequestKey, requestKey, StringComparison.Ordinal))
                    {
                        _appliedRuneSignature = GetRuneSignature(recommendation);
                        RuneStatusText = Text("Companion.Runes.Applied", "Rune page applied");
                        RuneApplyButtonText = Text("Companion.Runes.Applied.Button", "Applied");
                    }
                    WriteRuneOperation(
                        LogEventLevel.Information,
                        "rune.page.apply",
                        "Succeeded",
                        operationId,
                        championId,
                        queueId,
                        result.RunePageId,
                        stopwatch.ElapsedMilliseconds,
                        Text("Companion.Runes.Log.Applied", "Applied recommended rune page"));
                }
                else
                {
                    var rejected = result.Status is RunePageApplyStatus.ClientUnavailable or
                        RunePageApplyStatus.InvalidRecommendation;
                    var resultText = rejected
                        ? Text("Companion.Runes.ClientUnavailable", "League Client is unavailable")
                        : Text("Companion.Runes.ConfirmationFailed", "Unable to confirm the active rune page");
                    if (string.Equals(_runeRequestKey, requestKey, StringComparison.Ordinal))
                    {
                        RuneStatusText = resultText;
                    }
                    WriteRuneOperation(
                        rejected ? LogEventLevel.Warning : LogEventLevel.Error,
                        "rune.page.apply",
                        rejected ? "Rejected" : "Failed",
                        operationId,
                        championId,
                        queueId,
                        result.RunePageId,
                        stopwatch.ElapsedMilliseconds,
                        resultText,
                        result.Status.ToString());
                }
            }
            catch (OperationCanceledException)
            {
                stopwatch.Stop();
                var resultText = Text(
                    "Companion.Runes.Cancelled", "Rune application cancelled");
                if (string.Equals(_runeRequestKey, requestKey, StringComparison.Ordinal))
                {
                    RuneStatusText = resultText;
                }
                WriteRuneOperation(
                    LogEventLevel.Information,
                    "rune.page.apply",
                    "Cancelled",
                    operationId,
                    championId,
                    queueId,
                    0,
                    stopwatch.ElapsedMilliseconds,
                    resultText,
                    "Cancelled");
            }
            catch (Exception exception)
            {
                stopwatch.Stop();
                var resultText = Text(
                    "Companion.Runes.ApplyFailed", "Unable to apply rune page");
                if (string.Equals(_runeRequestKey, requestKey, StringComparison.Ordinal))
                {
                    RuneStatusText = resultText;
                }
                WriteRuneOperation(
                    LogEventLevel.Error,
                    "rune.page.apply",
                    "Failed",
                    operationId,
                    championId,
                    queueId,
                    0,
                    stopwatch.ElapsedMilliseconds,
                    resultText,
                    null,
                    exception);
            }
            finally
            {
                if (ReferenceEquals(_runeApplyCts, cancellationTokenSource))
                {
                    _runeApplyCts.Dispose();
                    _runeApplyCts = null;
                    if (string.Equals(_runeRequestKey, requestKey, StringComparison.Ordinal))
                    {
                        IsApplyingRune = false;
                        if (string.IsNullOrWhiteSpace(RuneApplyButtonText) ||
                            RuneApplyButtonText == Text(
                                "Companion.Runes.Applying.Button", "Applying..."))
                        {
                            RuneApplyButtonText = Text(
                                "Companion.Runes.Apply", "Apply to League Client");
                        }
                    }
                }
            }
        }

        private void ResetRuneRecommendation(bool hide)
        {
            CancelRuneOperations();
            _runeGeneration++;
            _runeRequestKey = string.Empty;
            _runeRecommendations = null;
            _selectedRuneRecommendation = null;
            _runeResources = new Dictionary<int, (string Name, string Icon)>();
            _runeChampionName = string.Empty;
            _isRuneChampionNameResolved = false;
            _appliedRuneSignature = string.Empty;
            IsRuneRecommendationLoading = false;
            HasRuneRecommendation = false;
            IsRuneRecommendationValid = false;
            IsApplyingRune = false;
            Replace(RunePerks, []);
            RuneChampionText = string.Empty;
            RuneStyleText = string.Empty;
            RuneStatsText = string.Empty;
            RuneSourceText = string.Empty;
            RuneApplyButtonText = Text("Companion.Runes.Apply", "Apply to League Client");
            if (hide)
            {
                RuneStatusText = string.Empty;
            }
        }

        private void CancelRuneOperations()
        {
            _runeRecommendationCts?.Cancel();
            _runeRecommendationCts?.Dispose();
            _runeRecommendationCts = null;
            _runeApplyCts?.Cancel();
        }

        private string FormatRuneStats(RuneRecommendationOption recommendation)
        {
            var pickRate = recommendation.PickRateBasisPoints / 100d;
            var winRate = recommendation.WinRateBasisPoints / 100d;
            return recommendation.SampleCount > 0
                ? string.Format(
                    Text("Companion.Runes.Stats.WithSample", "Pick {0:0.0}% · Win {1:0.0}% · {2:N0} games"),
                    pickRate,
                    winRate,
                    recommendation.SampleCount)
                : string.Format(
                    Text("Companion.Runes.Stats", "Pick {0:0.0}% · Win {1:0.0}%"),
                    pickRate,
                    winRate);
        }

        private string GetRuneLaneText(string lane)
        {
            return lane switch
            {
                "top" => Text("Companion.Runes.Lane.Top", "Top"),
                "jungle" => Text("Companion.Runes.Lane.Jungle", "Jungle"),
                "mid" => Text("Companion.Runes.Lane.Mid", "Mid"),
                "bottom" => Text("Companion.Runes.Lane.Bottom", "Bottom"),
                "support" => Text("Companion.Runes.Lane.Support", "Support"),
                "aram" => Text("Companion.Mode.Aram", "ARAM"),
                _ => string.Empty
            };
        }

        private string GetRuneStyleText(int styleId)
        {
            return styleId switch
            {
                8000 => Text("Companion.Runes.Style.Precision", "Precision"),
                8100 => Text("Companion.Runes.Style.Domination", "Domination"),
                8200 => Text("Companion.Runes.Style.Sorcery", "Sorcery"),
                8300 => Text("Companion.Runes.Style.Inspiration", "Inspiration"),
                8400 => Text("Companion.Runes.Style.Resolve", "Resolve"),
                _ => $"#{styleId}"
            };
        }

        private static string GetRuneSignature(RuneRecommendationOption recommendation)
        {
            return recommendation is null
                ? string.Empty
                : $"{recommendation.PrimaryStyleId}:{recommendation.SubStyleId}:" +
                  string.Join(',', recommendation.SelectedPerkIds);
        }

        private string GetManagedRunePageName()
        {
            if (!_isRuneChampionNameResolved)
            {
                throw new InvalidOperationException(
                    "A managed rune page cannot be named before the champion name is resolved.");
            }

            var recommendationName = _selectedRuneKind == RuneRecommendationKind.WinRate
                ? Text("Companion.Runes.PageName.WinRate", "Highest win rate runes")
                : Text("Companion.Runes.PageName.Popular", "Most popular runes");
            return $"{_runeChampionName} - {recommendationName} [Prometheus]";
        }

        private static void WriteRuneOperation(
            LogEventLevel level,
            string eventName,
            string outcome,
            Guid operationId,
            int championId,
            int queueId,
            long runePageId,
            long durationMs,
            string displayMessage,
            string errorCode = null,
            Exception exception = null)
        {
            var properties = new Dictionary<string, object>
            {
                ["TargetType"] = "RunePage",
                ["TargetId"] = "PrometheusManaged",
                ["ChampionId"] = championId,
                ["QueueId"] = queueId,
                ["DurationMs"] = durationMs
            };
            if (runePageId > 0)
            {
                properties["RunePageId"] = runePageId;
            }

            if (!string.IsNullOrWhiteSpace(errorCode))
            {
                properties["ErrorCode"] = errorCode;
            }

            if (exception is not null)
            {
                properties["ErrorType"] = exception.GetType().Name;
            }

            OperationLog.Write(
                level,
                eventName,
                "Rune",
                "Manual",
                outcome,
                operationId,
                "Companion",
                displayMessage,
                properties,
                exception);
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

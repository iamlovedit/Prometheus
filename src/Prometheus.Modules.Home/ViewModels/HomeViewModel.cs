using Newtonsoft.Json.Linq;
using Prism.Commands;
using Prism.Events;
using Prism.Regions;
using Prometheus.Core.Events;
using Prometheus.Core.Models;
using Prometheus.Core.Mvvm;
using Prometheus.Services.Interfaces.Client;
using Serilog;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;

namespace Prometheus.Modules.Home.ViewModels
{
    public class HomeViewModel : RegionViewModelBase
    {
        private readonly IEventAggregator _eventAggregator;
        private readonly IMatchService _matchService;
        private readonly IGameAutomationSettings _automationSettings;
        private readonly ISummonerService _summonerService;
        private readonly IGameResourceManager _gameResourceManager;
        private readonly IResourceService _resourceService;
        private readonly IClientService _clientService;
        private readonly DispatcherTimer _displayTimer;

        private CancellationTokenSource _dashboardCts;
        private int _dashboardVersion;
        private int _teamVersion;
        private bool _dashboardLoading;
        private LiveMatchSnapshot _snapshot = LiveMatchSnapshot.Empty;
        private DateTimeOffset _timerAnchor;
        private TimeSpan _timerValue;
        private TimerMode _timerMode;
        private Dictionary<int, string> _championNames;

        public HomeViewModel(
            IRegionManager regionManager,
            IEventAggregator eventAggregator,
            IMatchService matchService,
            ISummonerService summonerService,
            IGameResourceManager gameResourceManager,
            IResourceService resourceService,
            IClientService clientService) : base(regionManager)
        {
            _eventAggregator = eventAggregator;
            _matchService = matchService;
            _automationSettings = matchService.AutomationSettings;
            _summonerService = summonerService;
            _gameResourceManager = gameResourceManager;
            _resourceService = resourceService;
            _clientService = clientService;

            RecentMatches = [];
            MyTeam = [];
            TheirTeam = [];

            _displayTimer = new DispatcherTimer(DispatcherPriority.Background)
            {
                Interval = TimeSpan.FromSeconds(1)
            };
            _displayTimer.Tick += HandleDisplayTimerTick;
            _displayTimer.Start();

            _matchService.SnapshotChanged += HandleSnapshotChanged;
            _automationSettings.Changed += HandleAutomationChanged;
            _eventAggregator.GetEvent<LanguageSwitchedEvent>().Subscribe(HandleLanguageChanged);

            ApplySnapshot(_matchService.Current ?? LiveMatchSnapshot.Empty);
        }

        public ObservableCollection<HomeMatchItemViewModel> RecentMatches { get; }

        public ObservableCollection<HomeTeamMemberViewModel> MyTeam { get; }

        public ObservableCollection<HomeTeamMemberViewModel> TheirTeam { get; }

        private string _connectionStatus;
        public string ConnectionStatus
        {
            get => _connectionStatus;
            set => SetProperty(ref _connectionStatus, value);
        }

        private string _syncStatus;
        public string SyncStatus
        {
            get => _syncStatus;
            set => SetProperty(ref _syncStatus, value);
        }

        private string _updatedText;
        public string UpdatedText
        {
            get => _updatedText;
            set => SetProperty(ref _updatedText, value);
        }

        private string _phaseTitle;
        public string PhaseTitle
        {
            get => _phaseTitle;
            set => SetProperty(ref _phaseTitle, value);
        }

        private string _phaseDescription;
        public string PhaseDescription
        {
            get => _phaseDescription;
            set => SetProperty(ref _phaseDescription, value);
        }

        private string _phaseTimer = "--";
        public string PhaseTimer
        {
            get => _phaseTimer;
            set => SetProperty(ref _phaseTimer, value);
        }

        private string _phaseDetail;
        public string PhaseDetail
        {
            get => _phaseDetail;
            set => SetProperty(ref _phaseDetail, value);
        }

        private string _errorText;
        public string ErrorText
        {
            get => _errorText;
            set => SetProperty(ref _errorText, value);
        }

        private string _primaryActionText;
        public string PrimaryActionText
        {
            get => _primaryActionText;
            set => SetProperty(ref _primaryActionText, value);
        }

        private string _secondaryActionText;
        public string SecondaryActionText
        {
            get => _secondaryActionText;
            set => SetProperty(ref _secondaryActionText, value);
        }

        private bool _canPrimaryAction;
        public bool CanPrimaryAction
        {
            get => _canPrimaryAction;
            set => SetProperty(ref _canPrimaryAction, value);
        }

        private bool _canSecondaryAction = true;
        public bool CanSecondaryAction
        {
            get => _canSecondaryAction;
            set => SetProperty(ref _canSecondaryAction, value);
        }

        private string _summaryTitle;
        public string SummaryTitle
        {
            get => _summaryTitle;
            set => SetProperty(ref _summaryTitle, value);
        }

        private string _emptySummaryText;
        public string EmptySummaryText
        {
            get => _emptySummaryText;
            set => SetProperty(ref _emptySummaryText, value);
        }

        private bool _showRecentMatches;
        public bool ShowRecentMatches
        {
            get => _showRecentMatches;
            set => SetProperty(ref _showRecentMatches, value);
        }

        private bool _showTeamSummary;
        public bool ShowTeamSummary
        {
            get => _showTeamSummary;
            set => SetProperty(ref _showTeamSummary, value);
        }

        private bool _showEmptySummary = true;
        public bool ShowEmptySummary
        {
            get => _showEmptySummary;
            set => SetProperty(ref _showEmptySummary, value);
        }

        private string _heroBackground;
        public string HeroBackground
        {
            get => _heroBackground;
            set => SetProperty(ref _heroBackground, value);
        }

        private string _profileIcon;
        public string ProfileIcon
        {
            get => _profileIcon;
            set => SetProperty(ref _profileIcon, value);
        }

        private string _summonerName;
        public string SummonerName
        {
            get => _summonerName;
            set => SetProperty(ref _summonerName, value);
        }

        private string _summonerTag;
        public string SummonerTag
        {
            get => _summonerTag;
            set => SetProperty(ref _summonerTag, value);
        }

        private string _summonerLevel;
        public string SummonerLevel
        {
            get => _summonerLevel;
            set => SetProperty(ref _summonerLevel, value);
        }

        private string _soloTierText;
        public string SoloTierText
        {
            get => _soloTierText;
            set => SetProperty(ref _soloTierText, value);
        }

        private string _flexTierText;
        public string FlexTierText
        {
            get => _flexTierText;
            set => SetProperty(ref _flexTierText, value);
        }

        private string _automationStatus;
        public string AutomationStatus
        {
            get => _automationStatus;
            set => SetProperty(ref _automationStatus, value);
        }

        private bool _isConnected;
        public bool IsConnected
        {
            get => _isConnected;
            set => SetProperty(ref _isConnected, value);
        }

        private bool _isError;
        public bool IsError
        {
            get => _isError;
            set => SetProperty(ref _isError, value);
        }

        private bool _isLobbyStage;
        public bool IsLobbyStage
        {
            get => _isLobbyStage;
            set => SetProperty(ref _isLobbyStage, value);
        }

        private bool _isQueueStage;
        public bool IsQueueStage
        {
            get => _isQueueStage;
            set => SetProperty(ref _isQueueStage, value);
        }

        private bool _isReadyStage;
        public bool IsReadyStage
        {
            get => _isReadyStage;
            set => SetProperty(ref _isReadyStage, value);
        }

        private bool _isChampionStage;
        public bool IsChampionStage
        {
            get => _isChampionStage;
            set => SetProperty(ref _isChampionStage, value);
        }

        private bool _isGameStage;
        public bool IsGameStage
        {
            get => _isGameStage;
            set => SetProperty(ref _isGameStage, value);
        }

        public bool AutoAccept
        {
            get => _automationSettings.AutoAcceptReadyCheck;
            set
            {
                if (_automationSettings.AutoAcceptReadyCheck == value)
                {
                    return;
                }

                _automationSettings.AutoAcceptReadyCheck = value;
                RaisePropertyChanged();
                UpdateAutomationStatus();
            }
        }

        public bool AutoReconnect
        {
            get => _automationSettings.AutoReconnect;
            set
            {
                if (_automationSettings.AutoReconnect == value)
                {
                    return;
                }

                _automationSettings.AutoReconnect = value;
                RaisePropertyChanged();
                UpdateAutomationStatus();
            }
        }

        private DelegateCommand _primaryActionCommand;
        public DelegateCommand PrimaryActionCommand =>
            _primaryActionCommand ??= new DelegateCommand(ExecutePrimaryAction);

        private DelegateCommand _secondaryActionCommand;
        public DelegateCommand SecondaryActionCommand =>
            _secondaryActionCommand ??= new DelegateCommand(ExecuteSecondaryAction);

        private DelegateCommand _openClientCommand;
        public DelegateCommand OpenClientCommand =>
            _openClientCommand ??= new DelegateCommand(OpenClient);

        private DelegateCommand _openMatchCommand;
        public DelegateCommand OpenMatchCommand =>
            _openMatchCommand ??= new DelegateCommand(() => Navigate(MenuName.Match));

        private DelegateCommand _openCareerCommand;
        public DelegateCommand OpenCareerCommand =>
            _openCareerCommand ??= new DelegateCommand(() => Navigate(MenuName.Career));

        private DelegateCommand _openInventoryCommand;
        public DelegateCommand OpenInventoryCommand =>
            _openInventoryCommand ??= new DelegateCommand(() => Navigate(MenuName.Inventory));

        private DelegateCommand _openUtilityCommand;
        public DelegateCommand OpenUtilityCommand =>
            _openUtilityCommand ??= new DelegateCommand(() => Navigate(MenuName.Utility));

        private DelegateCommand<string> _searchCommand;
        public DelegateCommand<string> SearchCommand =>
            _searchCommand ??= new DelegateCommand<string>(SearchSummoner);

        public override void OnNavigatedTo(NavigationContext navigationContext)
        {
            ApplySnapshot(_matchService.Current ?? LiveMatchSnapshot.Empty);
        }

        public override void Destroy()
        {
            _displayTimer.Stop();
            _displayTimer.Tick -= HandleDisplayTimerTick;
            _matchService.SnapshotChanged -= HandleSnapshotChanged;
            _automationSettings.Changed -= HandleAutomationChanged;
            _eventAggregator.GetEvent<LanguageSwitchedEvent>().Unsubscribe(HandleLanguageChanged);
            CancelDashboardLoad();
            base.Destroy();
        }

        private void HandleSnapshotChanged(object sender, LiveMatchSnapshotChangedEventArgs args)
        {
            Dispatch(() => ApplySnapshot(args.Snapshot));
        }

        private void HandleAutomationChanged(object sender, EventArgs args)
        {
            Dispatch(() =>
            {
                RaisePropertyChanged(nameof(AutoAccept));
                RaisePropertyChanged(nameof(AutoReconnect));
                UpdateAutomationStatus();
            });
        }

        private void HandleLanguageChanged()
        {
            ApplySnapshot(_snapshot);
            UpdateAutomationStatus();
        }

        private void ApplySnapshot(LiveMatchSnapshot snapshot)
        {
            _snapshot = snapshot ?? LiveMatchSnapshot.Empty;
            _teamVersion++;

            IsConnected = _snapshot.ConnectionState == ConnectionState.Connected;
            IsError = _snapshot.ConnectionState == ConnectionState.Error ||
                      _snapshot.GameflowPhase == GameflowPhase.TerminatedInError;
            UpdatedText = string.Format(Text("HomePage.Updated"),
                _snapshot.UpdatedAt.ToLocalTime().ToString("HH:mm:ss"));
            SyncStatus = string.IsNullOrWhiteSpace(_snapshot.RawPhase)
                ? Text("HomePage.WaitingForPhase")
                : _snapshot.RawPhase;
            ErrorText = _snapshot.Error ?? string.Empty;

            ResetStageFlags();
            ShowRecentMatches = false;
            ShowTeamSummary = false;
            ShowEmptySummary = true;
            CanPrimaryAction = true;
            CanSecondaryAction = true;
            _timerMode = TimerMode.None;
            PhaseTimer = "--";

            switch (_snapshot.ConnectionState)
            {
                case ConnectionState.Connecting:
                case ConnectionState.Reconnecting:
                    ConfigureConnecting();
                    break;
                case ConnectionState.Disconnected:
                case ConnectionState.Stopping:
                    ConfigureDisconnected();
                    break;
                case ConnectionState.Error:
                    ConfigureError();
                    break;
                default:
                    ConfigurePhase(_snapshot.GameflowPhase);
                    break;
            }

            if (IsConnected)
            {
                _ = EnsureDashboardLoadedAsync();
            }
            else
            {
                CancelDashboardLoad();
                ClearDashboard();
            }

            UpdateAutomationStatus();
        }

        private void ConfigureConnecting()
        {
            ConnectionStatus = Text("HomePage.Connection.Connecting");
            PhaseTitle = Text("HomePage.Phase.Syncing.Title");
            PhaseDescription = Text("HomePage.Phase.Syncing.Description");
            PhaseDetail = Text("HomePage.Phase.Syncing.Detail");
            SummaryTitle = Text("HomePage.Summary.Status");
            EmptySummaryText = Text("HomePage.Summary.Syncing");
            PrimaryActionText = Text("HomePage.Action.Syncing");
            SecondaryActionText = Text("HomePage.OpenClient");
            CanPrimaryAction = false;
        }

        private void ConfigureDisconnected()
        {
            ConnectionStatus = Text("HomePage.Connection.Disconnected");
            PhaseTitle = Text("HomePage.Phase.Offline.Title");
            PhaseDescription = Text("HomePage.Phase.Offline.Description");
            PhaseDetail = Text("HomePage.Phase.Offline.Detail");
            SummaryTitle = Text("HomePage.Summary.Status");
            EmptySummaryText = Text("HomePage.Summary.Offline");
            PrimaryActionText = Text("HomePage.Action.Retry");
            SecondaryActionText = Text("Menu.Setting");
        }

        private void ConfigureError()
        {
            ConnectionStatus = Text("HomePage.Connection.Error");
            PhaseTitle = Text("HomePage.Phase.Error.Title");
            PhaseDescription = Text("HomePage.Phase.Error.Description");
            PhaseDetail = Text("HomePage.Phase.Error.Detail");
            SummaryTitle = Text("HomePage.Summary.Status");
            EmptySummaryText = Text("HomePage.Summary.Error");
            PrimaryActionText = Text("HomePage.Action.Retry");
            SecondaryActionText = Text("HomePage.OpenClient");
        }

        private void ConfigurePhase(GameflowPhase phase)
        {
            ConnectionStatus = Text("HomePage.Connection.Connected");
            switch (phase)
            {
                case GameflowPhase.None:
                    ConfigureIdle();
                    break;
                case GameflowPhase.Lobby:
                    ConfigureLobby();
                    break;
                case GameflowPhase.Matchmaking:
                    ConfigureMatchmaking();
                    break;
                case GameflowPhase.ReadyCheck:
                    ConfigureReadyCheck();
                    break;
                case GameflowPhase.ChampSelect:
                    ConfigureChampionSelect();
                    break;
                case GameflowPhase.GameStart:
                case GameflowPhase.InProgress:
                    ConfigureInGame();
                    break;
                case GameflowPhase.Reconnect:
                    ConfigureReconnect();
                    break;
                case GameflowPhase.WaitingForStats:
                case GameflowPhase.PreEndOfGame:
                    ConfigurePostGameLoading();
                    break;
                case GameflowPhase.EndOfGame:
                    ConfigurePostGame();
                    break;
                case GameflowPhase.TerminatedInError:
                    ConfigureError();
                    break;
                default:
                    ConfigureUnknown();
                    break;
            }
        }

        private void ConfigureIdle()
        {
            PhaseTitle = Text("HomePage.Phase.Idle.Title");
            PhaseDescription = Text("HomePage.Phase.Idle.Description");
            PhaseDetail = SummonerName ?? Text("HomePage.Phase.Idle.Detail");
            SummaryTitle = Text("HomePage.Summary.Recent");
            EmptySummaryText = Text("HomePage.Summary.NoMatches");
            ShowRecentMatches = RecentMatches.Count > 0;
            ShowEmptySummary = !ShowRecentMatches;
            PrimaryActionText = Text("HomePage.ViewCareer");
            SecondaryActionText = Text("Menu.Utility");
        }

        private void ConfigureLobby()
        {
            IsLobbyStage = true;
            var memberCount = _snapshot.Lobby?.Members?.Count ?? 0;
            PhaseTitle = Text("HomePage.Phase.Lobby.Title");
            PhaseDescription = Text("HomePage.Phase.Lobby.Description");
            PhaseDetail = string.Format(Text("HomePage.Phase.Lobby.Detail"), memberCount);
            SummaryTitle = Text("HomePage.Summary.Lobby");
            EmptySummaryText = _snapshot.Lobby?.GameConfig?.GameMode ?? Text("HomePage.Summary.WaitingTeam");
            PrimaryActionText = Text("HomePage.OpenClient");
            SecondaryActionText = Text("Menu.Utility");
        }

        private void ConfigureMatchmaking()
        {
            IsQueueStage = true;
            PhaseTitle = Text("HomePage.Phase.Matchmaking.Title");
            PhaseDescription = Text("HomePage.Phase.Matchmaking.Description");
            var queueName = _snapshot.Matchmaking?.Queue?.Name;
            PhaseDetail = string.IsNullOrWhiteSpace(queueName)
                ? Text("HomePage.Phase.Matchmaking.Detail")
                : queueName;
            SummaryTitle = Text("HomePage.Summary.Queue");
            EmptySummaryText = BuildQueueSummary();
            PrimaryActionText = Text("HomePage.OpenClient");
            SecondaryActionText = Text("Menu.Utility");
            SetElapsed(_snapshot.Matchmaking?.TimeInQueue ?? 0);
        }

        private void ConfigureReadyCheck()
        {
            IsReadyStage = true;
            PhaseTitle = Text("HomePage.Phase.Ready.Title");
            PhaseDescription = Text("HomePage.Phase.Ready.Description");
            var members = _snapshot.ReadyCheck?.Members ?? [];
            var accepted = members.Count(member =>
                string.Equals(member.PlayerResponse, "Accepted", StringComparison.OrdinalIgnoreCase));
            PhaseDetail = members.Count == 0
                ? Text("HomePage.Phase.Ready.Detail")
                : string.Format(Text("HomePage.Phase.Ready.Count"), accepted, members.Count);
            SummaryTitle = Text("HomePage.Summary.Ready");
            EmptySummaryText = _snapshot.ReadyCheck?.PlayerResponse ?? Text("HomePage.Phase.Ready.Detail");
            PrimaryActionText = Text("HomePage.Action.Accept");
            SecondaryActionText = Text("HomePage.OpenClient");
            CanPrimaryAction = !string.Equals(_snapshot.ReadyCheck?.PlayerResponse,
                "Accepted", StringComparison.OrdinalIgnoreCase);
            var left = _snapshot.ReadyCheck?.AdjustedTimeLeftInPhase ??
                       _snapshot.ReadyCheck?.Timer ?? 0;
            SetCountdown(left);
        }

        private void ConfigureChampionSelect()
        {
            IsChampionStage = true;
            PhaseTitle = Text("HomePage.Phase.Champion.Title");
            PhaseDescription = Text("HomePage.Phase.Champion.Description");
            PhaseDetail = _snapshot.ChampionSelect?.Timer?.Phase ?? Text("HomePage.Phase.Champion.Detail");
            SummaryTitle = Text("HomePage.Summary.Champion");
            EmptySummaryText = Text("HomePage.Summary.LoadingTeam");
            PrimaryActionText = Text("HomePage.OpenClient");
            SecondaryActionText = Text("HomePage.OpenDetails");
            var left = _snapshot.ChampionSelect?.Timer?.AdjustedTimeLeftInPhase ?? 0;
            SetCountdown(left);
            _ = UpdateChampionSelectTeamsAsync(_snapshot.ChampionSelect, _teamVersion);
        }

        private void ConfigureInGame()
        {
            IsGameStage = true;
            PhaseTitle = Text("HomePage.Phase.InGame.Title");
            PhaseDescription = Text("HomePage.Phase.InGame.Description");
            PhaseDetail = Text("HomePage.Phase.InGame.Detail");
            PhaseTimer = Text("HomePage.Live");
            SummaryTitle = Text("HomePage.Summary.Status");
            EmptySummaryText = Text("HomePage.Summary.InGame");
            PrimaryActionText = Text("HomePage.OpenClient");
            SecondaryActionText = Text("Menu.Utility");
        }

        private void ConfigureReconnect()
        {
            IsGameStage = true;
            PhaseTitle = Text("HomePage.Phase.Reconnect.Title");
            PhaseDescription = Text("HomePage.Phase.Reconnect.Description");
            PhaseDetail = Text("HomePage.Phase.Reconnect.Detail");
            SummaryTitle = Text("HomePage.Summary.Status");
            EmptySummaryText = Text("HomePage.Summary.Reconnect");
            PrimaryActionText = Text("HomePage.Action.Reconnect");
            SecondaryActionText = Text("HomePage.OpenClient");
        }

        private void ConfigurePostGameLoading()
        {
            IsGameStage = true;
            PhaseTitle = Text("HomePage.Phase.PostGameLoading.Title");
            PhaseDescription = Text("HomePage.Phase.PostGameLoading.Description");
            PhaseDetail = Text("HomePage.Phase.PostGameLoading.Detail");
            SummaryTitle = Text("HomePage.Summary.PostGame");
            EmptySummaryText = Text("HomePage.Summary.PostGameLoading");
            PrimaryActionText = Text("HomePage.Action.Syncing");
            SecondaryActionText = Text("HomePage.ViewCareer");
            CanPrimaryAction = false;
        }

        private void ConfigurePostGame()
        {
            IsGameStage = true;
            var player = _snapshot.PostGame?.LocalPlayer;
            PhaseTitle = player?.Won == true
                ? Text("HomePage.Phase.PostGame.Victory")
                : Text("HomePage.Phase.PostGame.Defeat");
            PhaseDescription = Text("HomePage.Phase.PostGame.Description");
            PhaseDetail = player is null
                ? Text("HomePage.Phase.PostGame.Detail")
                : $"KDA {player.Kills}/{player.Deaths}/{player.Assists}";
            SummaryTitle = Text("HomePage.Summary.PostGame");
            EmptySummaryText = _snapshot.PostGame?.GameMode ?? Text("HomePage.Summary.PostGameReady");
            PrimaryActionText = Text("HomePage.OpenDetails");
            SecondaryActionText = Text("HomePage.ViewCareer");
        }

        private void ConfigureUnknown()
        {
            PhaseTitle = Text("HomePage.Phase.Unknown.Title");
            PhaseDescription = Text("HomePage.Phase.Unknown.Description");
            PhaseDetail = string.IsNullOrWhiteSpace(_snapshot.RawPhase)
                ? Text("HomePage.WaitingForPhase")
                : _snapshot.RawPhase;
            SummaryTitle = Text("HomePage.Summary.Status");
            EmptySummaryText = Text("HomePage.Summary.Syncing");
            PrimaryActionText = Text("HomePage.OpenClient");
            SecondaryActionText = Text("HomePage.Action.Retry");
        }

        private async void ExecutePrimaryAction()
        {
            try
            {
                if (_snapshot.ConnectionState is ConnectionState.Disconnected or
                    ConnectionState.Error or ConnectionState.Reconnecting)
                {
                    await _matchService.StartAsync();
                    await _matchService.RefreshAsync();
                    return;
                }

                switch (_snapshot.GameflowPhase)
                {
                    case GameflowPhase.ReadyCheck:
                        await _matchService.AcceptReadyCheckAsync();
                        break;
                    case GameflowPhase.Reconnect:
                        await _matchService.ReconnectAsync();
                        break;
                    case GameflowPhase.None:
                        Navigate(MenuName.Career);
                        break;
                    case GameflowPhase.EndOfGame:
                        Navigate(MenuName.Match);
                        break;
                    default:
                        await _clientService.SetForgeground();
                        break;
                }
            }
            catch (Exception exception)
            {
                ErrorText = exception.Message;
                Log.Error(exception, "Home primary action failed");
            }
        }

        private async void ExecuteSecondaryAction()
        {
            try
            {
                if (_snapshot.ConnectionState == ConnectionState.Disconnected)
                {
                    Navigate(MenuName.Setting);
                    return;
                }

                switch (_snapshot.GameflowPhase)
                {
                    case GameflowPhase.ChampSelect:
                        Navigate(MenuName.Match);
                        break;
                    case GameflowPhase.EndOfGame:
                        Navigate(MenuName.Career);
                        break;
                    case GameflowPhase.Unknown:
                        await _matchService.RefreshAsync();
                        break;
                    default:
                        await _clientService.SetForgeground();
                        break;
                }
            }
            catch (Exception exception)
            {
                ErrorText = exception.Message;
                Log.Error(exception, "Home secondary action failed");
            }
        }

        private async void OpenClient()
        {
            try
            {
                await _clientService.SetForgeground();
            }
            catch (Exception exception)
            {
                ErrorText = exception.Message;
            }
        }

        private async void SearchSummoner(string name)
        {
            if (string.IsNullOrWhiteSpace(name) || !IsConnected)
            {
                return;
            }

            try
            {
                var summoner = await _summonerService.SearchSummonerByName(name.Trim());
                if (summoner is not null)
                {
                    _eventAggregator.GetEvent<SearchSummonerEvent>().Publish(summoner);
                }
            }
            catch (Exception exception)
            {
                ErrorText = exception.Message;
                Log.Error(exception, "Home summoner search failed");
            }
        }

        private void Navigate(MenuName menuName)
        {
            _eventAggregator.GetEvent<NavigateMenuEvent>().Publish(menuName);
        }

        private async Task EnsureDashboardLoadedAsync()
        {
            if (_dashboardLoading || !IsConnected || !string.IsNullOrWhiteSpace(SummonerName))
            {
                return;
            }

            CancelDashboardLoad();
            _dashboardCts = new CancellationTokenSource();
            var token = _dashboardCts.Token;
            var version = ++_dashboardVersion;
            _dashboardLoading = true;

            try
            {
                var summoner = await _summonerService.GetCurrentSummoner();
                token.ThrowIfCancellationRequested();
                if (summoner is null)
                {
                    return;
                }

                var profileTask = _gameResourceManager.GetProfileIconByIdAsync(summoner.ProfileIconId);
                var backgroundTask = LoadBackgroundAsync();
                var rankTask = _summonerService.GetRankStatsByPuuid(summoner.Puuid);
                var matchesTask = _summonerService.GetMatchesAsync(summoner.Puuid, 0, 4);

                await Task.WhenAll(profileTask, backgroundTask, rankTask, matchesTask);
                token.ThrowIfCancellationRequested();
                if (version != _dashboardVersion)
                {
                    return;
                }

                var ranks = ParseRanks(rankTask.Result);
                var matches = await BuildRecentMatchesAsync(matchesTask.Result, token);
                token.ThrowIfCancellationRequested();

                Dispatch(() =>
                {
                    if (version != _dashboardVersion)
                    {
                        return;
                    }

                    SummonerName = summoner.GameName ?? summoner.DisplayName ?? summoner.SummonerName;
                    SummonerTag = string.IsNullOrWhiteSpace(summoner.TagLine) ? string.Empty : $"#{summoner.TagLine}";
                    SummonerLevel = summoner.SummonerLevel.ToString();
                    ProfileIcon = profileTask.Result;
                    HeroBackground = backgroundTask.Result;
                    SoloTierText = ranks.solo?.DisplayTier ?? Text("Career.Rank.Tier.Unranked");
                    FlexTierText = ranks.flex?.DisplayTier ?? Text("Career.Rank.Tier.Unranked");
                    Replace(RecentMatches, matches);
                    if (_snapshot.GameflowPhase == GameflowPhase.None)
                    {
                        ConfigureIdle();
                    }
                });
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception exception)
            {
                Dispatch(() => ErrorText = exception.Message);
                Log.Error(exception, "Home dashboard loading failed");
            }
            finally
            {
                _dashboardLoading = false;
            }
        }

        private async Task<string> LoadBackgroundAsync()
        {
            var json = await _gameResourceManager.GetBackgroundSkinId();
            if (string.IsNullOrWhiteSpace(json))
            {
                return null;
            }

            var skinId = JObject.Parse(json)["backgroundSkinId"]?.ToObject<int>() ?? 0;
            return skinId <= 0 ? null : await _gameResourceManager.GetBackgroundSkinByIdAsync(skinId);
        }

        private (Rank solo, Rank flex) ParseRanks(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                return (null, null);
            }

            var queueMap = JObject.Parse(json)["queueMap"];
            return (
                queueMap?["RANKED_SOLO_5x5"]?.ToObject<Rank>(),
                queueMap?["RANKED_FLEX_SR"]?.ToObject<Rank>());
        }

        private async Task<IReadOnlyList<HomeMatchItemViewModel>> BuildRecentMatchesAsync(
            IReadOnlyList<Match> matches, CancellationToken token)
        {
            if (matches is null)
            {
                return Array.Empty<HomeMatchItemViewModel>();
            }

            var tasks = matches.Take(5).Select(async match =>
            {
                token.ThrowIfCancellationRequested();
                var participant = match.Participants?.FirstOrDefault();
                var icon = participant is null
                    ? null
                    : await _gameResourceManager.GetChampoinIconByIdAsync(participant.ChampionId);
                var stats = participant?.Stats;
                return new HomeMatchItemViewModel
                {
                    ChampionIcon = icon,
                    IsWin = stats?.Win == true,
                    ResultText = stats?.Win == true
                        ? Text("Career.Match.Victory")
                        : Text("Career.Match.Defeated"),
                    GameMode = match.DisplayGameMode ?? match.GameMode,
                    KdaText = stats is null ? "--" : $"{stats.Kills}/{stats.Deaths}/{stats.Assists}",
                    CreationText = match.CreationDate?.ToString("MM-dd HH:mm") ?? string.Empty
                };
            });

            return await Task.WhenAll(tasks);
        }

        private async Task UpdateChampionSelectTeamsAsync(ChampionSelectSnapshot championSelect, int version)
        {
            if (championSelect is null)
            {
                return;
            }

            try
            {
                await EnsureChampionNamesAsync();
                var myTasks = (championSelect.MyTeam ?? []).Select(member =>
                    BuildTeamMemberAsync(member, championSelect.LocalPlayerCellId, false));
                var theirTasks = (championSelect.TheirTeam ?? []).Select(member =>
                    BuildTeamMemberAsync(member, championSelect.LocalPlayerCellId, member.ChampionId <= 0));
                var myTeam = await Task.WhenAll(myTasks);
                var theirTeam = await Task.WhenAll(theirTasks);

                Dispatch(() =>
                {
                    if (version != _teamVersion || _snapshot.GameflowPhase != GameflowPhase.ChampSelect)
                    {
                        return;
                    }

                    Replace(MyTeam, myTeam);
                    Replace(TheirTeam, theirTeam);
                    ShowTeamSummary = MyTeam.Count > 0 || TheirTeam.Count > 0;
                    ShowEmptySummary = !ShowTeamSummary;
                });
            }
            catch (Exception exception)
            {
                Log.Warning(exception, "Unable to enrich champion-select summary");
            }
        }

        private async Task EnsureChampionNamesAsync()
        {
            if (_championNames is not null)
            {
                return;
            }

            var champions = await _gameResourceManager.GetChampionSummarysAsync();
            _championNames = champions?.ToDictionary(champion => champion.Id,
                champion => champion.Name) ?? [];
        }

        private async Task<HomeTeamMemberViewModel> BuildTeamMemberAsync(
            ChampionSelectTeamMemberSnapshot member, long localPlayerCellId, bool hidden)
        {
            var championIconTask = member.ChampionId > 0
                ? _gameResourceManager.GetChampoinIconByIdAsync(member.ChampionId)
                : Task.FromResult<string>(null);
            var spell1Task = member.Spell1Id > 0
                ? _gameResourceManager.GetSpellIconByIdAsync(member.Spell1Id)
                : Task.FromResult<string>(null);
            var spell2Task = member.Spell2Id > 0
                ? _gameResourceManager.GetSpellIconByIdAsync(member.Spell2Id)
                : Task.FromResult<string>(null);
            await Task.WhenAll(championIconTask, spell1Task, spell2Task);

            var isLocal = member.CellId == localPlayerCellId;
            var displayName = hidden
                ? Text("HomePage.HiddenPlayer")
                : isLocal
                    ? Text("HomePage.LocalPlayer")
                    : _championNames.TryGetValue(member.ChampionId, out var championName)
                        ? championName
                        : string.Format(Text("HomePage.TeamSlot"), member.CellId + 1);

            return new HomeTeamMemberViewModel
            {
                CellId = member.CellId,
                ChampionIcon = championIconTask.Result,
                Spell1Icon = spell1Task.Result,
                Spell2Icon = spell2Task.Result,
                DisplayName = displayName,
                Position = FormatPosition(member.AssignedPosition),
                IsLocalPlayer = isLocal,
                IsHidden = hidden
            };
        }

        private string FormatPosition(string position)
        {
            if (string.IsNullOrWhiteSpace(position))
            {
                return Text("HomePage.Position.Unknown");
            }

            var key = position.Trim().ToUpperInvariant() switch
            {
                "TOP" => "HomePage.Position.Top",
                "JUNGLE" => "HomePage.Position.Jungle",
                "MIDDLE" or "MID" => "HomePage.Position.Middle",
                "BOTTOM" or "BOT" => "HomePage.Position.Bottom",
                "UTILITY" or "SUPPORT" => "HomePage.Position.Utility",
                _ => null
            };
            return key is null ? position : Text(key);
        }

        private string BuildQueueSummary()
        {
            var matchmaking = _snapshot.Matchmaking;
            if (matchmaking?.LowPriorityData?.IsLowPriority == true)
            {
                return string.Format(Text("HomePage.Queue.LowPriority"),
                    matchmaking.LowPriorityData.PenaltyTime);
            }

            if (matchmaking?.EstimatedQueueTime > 0)
            {
                return string.Format(Text("HomePage.Queue.Estimated"),
                    FormatDuration(TimeSpan.FromSeconds(matchmaking.EstimatedQueueTime)));
            }

            return Text("HomePage.Summary.QueueWaiting");
        }

        private void UpdateAutomationStatus()
        {
            if (AutoAccept && AutoReconnect)
            {
                AutomationStatus = Text("HomePage.Automation.BothOn");
            }
            else if (AutoAccept)
            {
                AutomationStatus = Text("HomePage.Automation.AcceptOn");
            }
            else if (AutoReconnect)
            {
                AutomationStatus = Text("HomePage.Automation.ReconnectOn");
            }
            else
            {
                AutomationStatus = Text("HomePage.Automation.Off");
            }
        }

        private void SetCountdown(double value)
        {
            var seconds = value > 300 ? value / 1000d : value;
            _timerMode = TimerMode.Countdown;
            _timerAnchor = DateTimeOffset.Now;
            _timerValue = TimeSpan.FromSeconds(Math.Max(0, seconds));
            UpdateTimerText();
        }

        private void SetElapsed(double seconds)
        {
            _timerMode = TimerMode.Elapsed;
            _timerAnchor = DateTimeOffset.Now;
            _timerValue = TimeSpan.FromSeconds(Math.Max(0, seconds));
            UpdateTimerText();
        }

        private void HandleDisplayTimerTick(object sender, EventArgs args)
        {
            UpdateTimerText();
        }

        private void UpdateTimerText()
        {
            var elapsed = DateTimeOffset.Now - _timerAnchor;
            var value = _timerMode switch
            {
                TimerMode.Countdown => _timerValue - elapsed,
                TimerMode.Elapsed => _timerValue + elapsed,
                _ => TimeSpan.MinValue
            };

            if (_timerMode == TimerMode.None)
            {
                return;
            }

            if (value < TimeSpan.Zero)
            {
                value = TimeSpan.Zero;
            }

            PhaseTimer = FormatDuration(value);
        }

        private static string FormatDuration(TimeSpan value)
        {
            return value.TotalHours >= 1
                ? value.ToString(@"h\:mm\:ss")
                : value.ToString(@"m\:ss");
        }

        private void CancelDashboardLoad()
        {
            _dashboardVersion++;
            _dashboardCts?.Cancel();
            _dashboardCts?.Dispose();
            _dashboardCts = null;
            _dashboardLoading = false;
        }

        private void ClearDashboard()
        {
            SummonerName = Text("HomePage.NoSummoner");
            SummonerTag = string.Empty;
            SummonerLevel = "--";
            ProfileIcon = null;
            HeroBackground = null;
            SoloTierText = "--";
            FlexTierText = "--";
            RecentMatches.Clear();
            MyTeam.Clear();
            TheirTeam.Clear();
        }

        private void ResetStageFlags()
        {
            IsLobbyStage = false;
            IsQueueStage = false;
            IsReadyStage = false;
            IsChampionStage = false;
            IsGameStage = false;
        }

        private string Text(string key)
        {
            return _resourceService.FindResource<string>(key);
        }

        private static void Replace<T>(ObservableCollection<T> target, IEnumerable<T> values)
        {
            target.Clear();
            foreach (var value in values ?? Enumerable.Empty<T>())
            {
                target.Add(value);
            }
        }

        private static void Dispatch(Action action)
        {
            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher is not null && !dispatcher.CheckAccess())
            {
                dispatcher.BeginInvoke(action);
                return;
            }

            action();
        }

        private enum TimerMode
        {
            None,
            Countdown,
            Elapsed
        }
    }
}

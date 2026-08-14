using HandyControl.Controls;
using Prism.Commands;
using Prism.Events;
using Prism.Modularity;
using Prism.Mvvm;
using Prism.Regions;
using Prometheus.Core;
using Prometheus.Core.Events;
using Prometheus.Core.Models;
using Prometheus.Core.Mvvm;
using Prometheus.Core.Tasks;
using Prometheus.Desktop.Services;
using Prometheus.Modules.Inventory;
using Prometheus.Modules.Match;
using Prometheus.Modules.Search;
using Prometheus.Modules.Summoner;
using Prometheus.Modules.Utility;
using Prometheus.Core.Logging;
using Prometheus.Services.Interfaces.Client;
using Prometheus.Services.Interfaces.Updates;
using Serilog;
using Serilog.Events;
using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Threading;

namespace Prometheus.ViewModels
{
    public class MainWindowViewModel : BindableBase
    {
        private const string PrometheusTitle = "Prometheus";

        private readonly IRegionManager _regionManager;
        private readonly IEventAggregator _eventAggregator;
        private readonly IModuleManager _moduleManager;
        private readonly IMatchService _matchService;
        private readonly IClientService _clientService;
        private readonly IClientListener _clientListener;
        private readonly IResourceService _resourceService;
        private readonly IProfilePresentationStartupService _profilePresentationStartupService;
        private readonly IGameAutomationSettings _automationSettings;
        private readonly ILcuCompanionSettings _companionSettings;
        private readonly IUpdateService _updateService;
        private readonly IGameService _gameService;
        private readonly IQuickMatchSettings _quickMatchSettings;
        private readonly ILcuCompanionWindowController _lcuCompanionWindowController;
        private readonly LatestValueDispatcher<LiveMatchSnapshot> _snapshotDispatcher;
        private CancellationTokenSource _quickMatchLobbyCts;
        private LiveMatchSnapshot _snapshot = LiveMatchSnapshot.Empty;
        private bool _isCreatingQuickMatchLobby;
        private int _selectedQuickMatchQueueId;
        private long _lastAutoShownGameId;
        private bool _autoShowHandledWithoutGameId;

        private GameflowPhase _lastObservedPhase = GameflowPhase.Unknown;
        private MenuName _currentMenu = MenuName.Home;

        public MainWindowViewModel(
            IRegionManager regionManager,
            IEventAggregator eventAggregator,
            IModuleManager moduleManager,
            IMatchService matchService,
            IClientService clientService,
            IClientListener clientListener,
            IResourceService resourceService,
            IProfilePresentationStartupService profilePresentationStartupService,
            IGameAutomationSettings automationSettings,
            IUpdateService updateService,
            IGameService gameService,
            IQuickMatchSettings quickMatchSettings,
            ILcuCompanionSettings companionSettings,
            ILcuCompanionWindowController lcuCompanionWindowController = null)
        {
            _regionManager = regionManager;
            _eventAggregator = eventAggregator;
            _moduleManager = moduleManager;
            _matchService = matchService;
            _clientService = clientService;
            _clientListener = clientListener;
            _resourceService = resourceService;
            _profilePresentationStartupService = profilePresentationStartupService;
            _automationSettings = automationSettings;
            _updateService = updateService;
            _gameService = gameService;
            _quickMatchSettings = quickMatchSettings;
            _companionSettings = companionSettings;
            _lcuCompanionWindowController = lcuCompanionWindowController;
            _selectedQuickMatchQueueId = NormalizeQuickMatchQueueId(
                _quickMatchSettings.QueueId);
            _snapshotDispatcher = new LatestValueDispatcher<LiveMatchSnapshot>(
                action => Dispatch(action, DispatcherPriority.Background),
                ApplySnapshot);

            _matchService.SnapshotChanged += HandleSnapshotChanged;
            _automationSettings.Changed += HandleAutomationSettingsChanged;
            _companionSettings.PropertyChanged += HandleCompanionSettingsChanged;
            _quickMatchSettings.Changed += HandleQuickMatchSettingsChanged;
            _updateService.StateChanged += HandleUpdateStateChanged;
            _eventAggregator.GetEvent<NavigateMenuEvent>().Subscribe(HandleNavigateMenu);
            _eventAggregator.GetEvent<SearchSummonerEvent>().Subscribe(HandleSearchSummoner);
            _eventAggregator.GetEvent<TitleChangeEvent>().Subscribe(HandleTitleChange);
            _eventAggregator.GetEvent<LanguageSwitchedEvent>().Subscribe(HandleLanguageChanged);
            _eventAggregator.GetEvent<WindowClosingEvent>().Subscribe(HandleWindowClosing);

            UpdateTrayLocalizedText();
            UpdateTrayState(_matchService.Current ?? LiveMatchSnapshot.Empty);
            RefreshUpdateState();
        }

        private string _title = PrometheusTitle;
        public string Title
        {
            get => _title;
            set => SetProperty(ref _title, value);
        }

        public bool IsHomeSelected => _currentMenu == MenuName.Home;
        public bool IsCareerSelected => _currentMenu == MenuName.Career;
        public bool IsInventorySelected => _currentMenu == MenuName.Inventory;
        public bool IsSearchSelected => _currentMenu == MenuName.Search;
        public bool IsMatchSelected => _currentMenu == MenuName.Match;
        public bool IsUtilitySelected => _currentMenu == MenuName.Utility;
        public bool IsSettingSelected => _currentMenu == MenuName.Setting;
        public bool IsClientNavigationAvailable =>
            _snapshot.ConnectionState == ConnectionState.Connected;

        private bool _hasAvailableUpdate;
        public bool HasAvailableUpdate
        {
            get => _hasAvailableUpdate;
            private set => SetProperty(ref _hasAvailableUpdate, value);
        }

        private string _trayClientStatus;
        public string TrayClientStatus
        {
            get => _trayClientStatus;
            private set => SetProperty(ref _trayClientStatus, value);
        }

        private string _trayGameflowStatus;
        public string TrayGameflowStatus
        {
            get => _trayGameflowStatus;
            private set => SetProperty(ref _trayGameflowStatus, value);
        }

        private string _trayToolTip = PrometheusTitle;
        public string TrayToolTip
        {
            get => _trayToolTip;
            private set => SetProperty(ref _trayToolTip, value);
        }

        private bool _isTrayMatchAvailable;
        public bool IsTrayMatchAvailable
        {
            get => _isTrayMatchAvailable;
            private set => SetProperty(ref _isTrayMatchAvailable, value);
        }

        private bool _isTrayReadyCheckAvailable;
        public bool IsTrayReadyCheckAvailable
        {
            get => _isTrayReadyCheckAvailable;
            private set => SetProperty(ref _isTrayReadyCheckAvailable, value);
        }

        private bool _isTrayQuickMatchAvailable;
        public bool IsTrayQuickMatchAvailable
        {
            get => _isTrayQuickMatchAvailable;
            private set => SetProperty(ref _isTrayQuickMatchAvailable, value);
        }

        private string _trayQuickMatchText;
        public string TrayQuickMatchText
        {
            get => _trayQuickMatchText;
            private set => SetProperty(ref _trayQuickMatchText, value);
        }

        private string _trayQuickMatchLastText;
        public string TrayQuickMatchLastText
        {
            get => _trayQuickMatchLastText;
            private set => SetProperty(ref _trayQuickMatchLastText, value);
        }

        private string _trayQuickMatchSoloDuoText;
        public string TrayQuickMatchSoloDuoText
        {
            get => _trayQuickMatchSoloDuoText;
            private set => SetProperty(ref _trayQuickMatchSoloDuoText, value);
        }

        private string _trayQuickMatchFlexText;
        public string TrayQuickMatchFlexText
        {
            get => _trayQuickMatchFlexText;
            private set => SetProperty(ref _trayQuickMatchFlexText, value);
        }

        private string _trayQuickMatchAramText;
        public string TrayQuickMatchAramText
        {
            get => _trayQuickMatchAramText;
            private set => SetProperty(ref _trayQuickMatchAramText, value);
        }

        private string _trayQuickMatchHextechAramText;
        public string TrayQuickMatchHextechAramText
        {
            get => _trayQuickMatchHextechAramText;
            private set => SetProperty(ref _trayQuickMatchHextechAramText, value);
        }

        private string _trayShowMainWindowText;
        public string TrayShowMainWindowText
        {
            get => _trayShowMainWindowText;
            private set => SetProperty(ref _trayShowMainWindowText, value);
        }

        private string _trayOpenMatchText;
        public string TrayOpenMatchText
        {
            get => _trayOpenMatchText;
            private set => SetProperty(ref _trayOpenMatchText, value);
        }

        private string _trayAcceptText;
        public string TrayAcceptText
        {
            get => _trayAcceptText;
            private set => SetProperty(ref _trayAcceptText, value);
        }

        private string _trayAutomationText;
        public string TrayAutomationText
        {
            get => _trayAutomationText;
            private set => SetProperty(ref _trayAutomationText, value);
        }

        private string _trayAutoAcceptText;
        public string TrayAutoAcceptText
        {
            get => _trayAutoAcceptText;
            private set => SetProperty(ref _trayAutoAcceptText, value);
        }

        private string _trayAutoReconnectText;
        public string TrayAutoReconnectText
        {
            get => _trayAutoReconnectText;
            private set => SetProperty(ref _trayAutoReconnectText, value);
        }

        private string _trayAramSwapText;
        public string TrayAramSwapText
        {
            get => _trayAramSwapText;
            private set => SetProperty(ref _trayAramSwapText, value);
        }

        private string _trayCompanionText;
        public string TrayCompanionText
        {
            get => _trayCompanionText;
            private set => SetProperty(ref _trayCompanionText, value);
        }

        private string _traySettingsText;
        public string TraySettingsText
        {
            get => _traySettingsText;
            private set => SetProperty(ref _traySettingsText, value);
        }

        private string _trayExitText;
        public string TrayExitText
        {
            get => _trayExitText;
            private set => SetProperty(ref _trayExitText, value);
        }

        public bool IsTrayAutoAcceptEnabled
        {
            get => _automationSettings.AutoAcceptReadyCheck;
            set
            {
                if (_automationSettings.AutoAcceptReadyCheck != value)
                {
                    _automationSettings.AutoAcceptReadyCheck = value;
                }
            }
        }

        public bool IsTrayAutoReconnectEnabled
        {
            get => _automationSettings.AutoReconnect;
            set
            {
                if (_automationSettings.AutoReconnect != value)
                {
                    _automationSettings.AutoReconnect = value;
                }
            }
        }

        public bool IsTrayAramSwapEnabled
        {
            get => _automationSettings.AutoSwapAramBench;
            set
            {
                var oldValue = _automationSettings.AutoSwapAramBench;
                if (oldValue == value)
                {
                    return;
                }

                _automationSettings.AutoSwapAramBench = value;
                var persisted = _automationSettings.LastPersistenceSucceeded;
                OperationLog.Write(
                    persisted ? LogEventLevel.Information : LogEventLevel.Error,
                    "automation.aram_bench_swap.changed",
                    "Automation",
                    "Manual",
                    persisted ? "Succeeded" : "Failed",
                    Guid.NewGuid(),
                    "Tray",
                    persisted
                        ? value
                            ? "Automatic ARAM champion swapping was enabled."
                            : "Automatic ARAM champion swapping was disabled."
                        : "The automatic ARAM champion swap setting could not be saved.",
                    new Dictionary<string, object>
                    {
                        ["OldValue"] = oldValue,
                        ["NewValue"] = value
                    });
                if (!persisted)
                {
                    Growl.Warning(Text("Utility.AramSwap.PersistenceFailed"));
                }
            }
        }

        public bool IsTrayCompanionEnabled
        {
            get => _companionSettings.IsEnabled;
            set
            {
                if (_companionSettings.IsEnabled == value)
                {
                    return;
                }

                _companionSettings.IsEnabled = value;
                if (!_companionSettings.LastPersistenceSucceeded)
                {
                    Growl.Warning(Text("Utility.Companion.PersistenceFailed"));
                }
            }
        }

        private DelegateCommand _loadedCommand;
        public DelegateCommand LoadedCommand =>
            _loadedCommand ??= new DelegateCommand(ExecuteLoadedCommand);

        private async void ExecuteLoadedCommand()
        {
            _ = CheckForUpdatesAfterStartupAsync();
            _lcuCompanionWindowController?.Start();
            try
            {
                _profilePresentationStartupService.Start();
                await _matchService.StartAsync();
            }
            catch (Exception exception)
            {
                Log.Error(exception, "Unable to start live match coordinator");
            }
        }

        private async Task CheckForUpdatesAfterStartupAsync()
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(15));
                await _updateService.CheckAsync(false);
            }
            catch (Exception exception)
            {
                Log.Warning(exception, "Unable to complete the automatic update check");
            }
        }

        private DelegateCommand _homeCommand;
        public DelegateCommand HomeCommand =>
            _homeCommand ??= new DelegateCommand(() => Navigate(MenuName.Home));

        private DelegateCommand _careerCommand;
        public DelegateCommand CareerCommand =>
            _careerCommand ??= new DelegateCommand(
                () => Navigate(MenuName.Career),
                CanNavigateToClientFeature);

        private DelegateCommand _inventoryCommand;
        public DelegateCommand InventoryCommand =>
            _inventoryCommand ??= new DelegateCommand(
                () => Navigate(MenuName.Inventory),
                CanNavigateToClientFeature);

        private DelegateCommand _searchCommand;
        public DelegateCommand SearchCommand =>
            _searchCommand ??= new DelegateCommand(
                () => Navigate(MenuName.Search),
                CanNavigateToClientFeature);

        private DelegateCommand _matchCommand;
        public DelegateCommand MatchCommand =>
            _matchCommand ??= new DelegateCommand(
                () => Navigate(MenuName.Match),
                CanNavigateToClientFeature);

        private DelegateCommand _utilityCommand;
        public DelegateCommand UtilityCommand =>
            _utilityCommand ??= new DelegateCommand(
                () => Navigate(MenuName.Utility),
                CanNavigateToClientFeature);

        private DelegateCommand _settingCommand;
        public DelegateCommand SettingCommand =>
            _settingCommand ??= new DelegateCommand(() => Navigate(MenuName.Setting));

        private DelegateCommand _showMainWindowCommand;
        public DelegateCommand ShowMainWindowCommand =>
            _showMainWindowCommand ??= new DelegateCommand(ShowMainWindow);

        private DelegateCommand _openMatchFromTrayCommand;
        public DelegateCommand OpenMatchFromTrayCommand =>
            _openMatchFromTrayCommand ??= new DelegateCommand(() => OpenTrayView(MenuName.Match));

        private DelegateCommand _openSettingsFromTrayCommand;
        public DelegateCommand OpenSettingsFromTrayCommand =>
            _openSettingsFromTrayCommand ??= new DelegateCommand(() => OpenTrayView(MenuName.Setting));

        private DelegateCommand _acceptReadyCheckFromTrayCommand;
        public DelegateCommand AcceptReadyCheckFromTrayCommand =>
            _acceptReadyCheckFromTrayCommand ??= new DelegateCommand(ExecuteAcceptReadyCheckFromTray);

        private DelegateCommand _quickStartLastFromTrayCommand;
        public DelegateCommand QuickStartLastFromTrayCommand =>
            _quickStartLastFromTrayCommand ??= new DelegateCommand(
                () => CreateQuickMatchLobbyAsync(
                        _selectedQuickMatchQueueId,
                        GetQuickMatchQueueNameResourceKey(_selectedQuickMatchQueueId))
                    .Observe("Creating the selected quick-match lobby from the tray"),
                CanCreateQuickMatchLobby);

        private DelegateCommand _quickStartSoloDuoFromTrayCommand;
        public DelegateCommand QuickStartSoloDuoFromTrayCommand =>
            _quickStartSoloDuoFromTrayCommand ??= CreateQuickMatchCommand(
                GameQueueIds.RankedSoloDuo,
                "HomePage.QuickMatch.SoloDuo",
                "Creating a ranked solo/duo lobby from the tray");

        private DelegateCommand _quickStartFlexFromTrayCommand;
        public DelegateCommand QuickStartFlexFromTrayCommand =>
            _quickStartFlexFromTrayCommand ??= CreateQuickMatchCommand(
                GameQueueIds.RankedFlex,
                "HomePage.QuickMatch.Flex",
                "Creating a ranked flex lobby from the tray");

        private DelegateCommand _quickStartAramFromTrayCommand;
        public DelegateCommand QuickStartAramFromTrayCommand =>
            _quickStartAramFromTrayCommand ??= CreateQuickMatchCommand(
                GameQueueIds.Aram,
                "HomePage.QuickMatch.Aram",
                "Creating an ARAM lobby from the tray");

        private DelegateCommand _quickStartHextechAramFromTrayCommand;
        public DelegateCommand QuickStartHextechAramFromTrayCommand =>
            _quickStartHextechAramFromTrayCommand ??= CreateQuickMatchCommand(
                GameQueueIds.HextechAram,
                "HomePage.QuickMatch.HextechAram",
                "Creating a Hextech ARAM lobby from the tray");

        private void HandleNavigateMenu(MenuName menuName)
        {
            Dispatch(() => Navigate(menuName));
        }

        private void HandleSearchSummoner(SummonerAccount summoner)
        {
            Dispatch(() => Navigate(MenuName.Search, summoner));
        }

        private void HandleTitleChange(string value)
        {
            Title = string.IsNullOrWhiteSpace(value)
                ? PrometheusTitle
                : $"{PrometheusTitle} -- {value}";
        }

        private void HandleLanguageChanged()
        {
            UpdateWindowTitle(_currentMenu);
            UpdateTrayLocalizedText();
            UpdateTrayState(_matchService.Current ?? LiveMatchSnapshot.Empty);
        }

        private void HandleQuickMatchSettingsChanged(object sender, EventArgs e)
        {
            Dispatch(() =>
            {
                _selectedQuickMatchQueueId = NormalizeQuickMatchQueueId(
                    _quickMatchSettings.QueueId);
                UpdateTrayQuickMatchText();
            });
        }

        private void HandleWindowClosing(ApplicationShutdownContext shutdownContext)
        {
            ArgumentNullException.ThrowIfNull(shutdownContext);
            shutdownContext.Register(StopAsync());
        }

        private async Task StopAsync()
        {
            _lcuCompanionWindowController?.Stop();
            _matchService.SnapshotChanged -= HandleSnapshotChanged;
            _automationSettings.Changed -= HandleAutomationSettingsChanged;
            _companionSettings.PropertyChanged -= HandleCompanionSettingsChanged;
            _quickMatchSettings.Changed -= HandleQuickMatchSettingsChanged;
            _updateService.StateChanged -= HandleUpdateStateChanged;
            _eventAggregator.GetEvent<NavigateMenuEvent>().Unsubscribe(HandleNavigateMenu);
            _eventAggregator.GetEvent<SearchSummonerEvent>().Unsubscribe(HandleSearchSummoner);
            _eventAggregator.GetEvent<TitleChangeEvent>().Unsubscribe(HandleTitleChange);
            _eventAggregator.GetEvent<LanguageSwitchedEvent>().Unsubscribe(HandleLanguageChanged);
            _eventAggregator.GetEvent<WindowClosingEvent>().Unsubscribe(HandleWindowClosing);
            _quickMatchLobbyCts?.Cancel();
            try
            {
                _profilePresentationStartupService.Stop();
                await _matchService.StopAsync();
                _clientListener.Close();
            }
            catch (Exception exception)
            {
                Log.Warning(exception, "Unable to stop live match coordinator cleanly");
            }
        }

        private void HandleSnapshotChanged(object sender, LiveMatchSnapshotChangedEventArgs args)
        {
            _snapshotDispatcher.Publish(args?.Snapshot ?? LiveMatchSnapshot.Empty);
        }

        private void ApplySnapshot(LiveMatchSnapshot snapshot)
        {
            UpdateTrayState(snapshot);
            if (RequiresClientConnection(_currentMenu) &&
                IsTerminalClientUnavailable(snapshot.ConnectionState))
            {
                Navigate(MenuName.Home);
            }

            var phase = snapshot.GameflowPhase;
            var phaseChanged = _lastObservedPhase != phase;
            _lastObservedPhase = phase;

            if (phaseChanged && phase == GameflowPhase.ChampSelect)
            {
                Navigate(MenuName.Match);
            }

            TryAutoShowMatch(snapshot);

            if (phaseChanged &&
                phase is GameflowPhase.ReadyCheck or GameflowPhase.ChampSelect)
            {
                _ = FlashClientSafelyAsync();
            }
        }

        private void HandleAutomationSettingsChanged(object sender, EventArgs e)
        {
            Dispatch(() =>
            {
                RaisePropertyChanged(nameof(IsTrayAutoAcceptEnabled));
                RaisePropertyChanged(nameof(IsTrayAutoReconnectEnabled));
                RaisePropertyChanged(nameof(IsTrayAramSwapEnabled));
            });
        }

        private void HandleCompanionSettingsChanged(
            object sender,
            PropertyChangedEventArgs args)
        {
            var propertyName = args?.PropertyName;
            var companionChanged = string.IsNullOrEmpty(propertyName) ||
                propertyName == nameof(ILcuCompanionSettings.IsEnabled);
            var autoShowChanged = string.IsNullOrEmpty(propertyName) ||
                propertyName == nameof(
                    ILcuCompanionSettings.AutoShowMatchOnGameStart);
            if (companionChanged || autoShowChanged)
            {
                Dispatch(() =>
                {
                    if (companionChanged)
                    {
                        RaisePropertyChanged(nameof(IsTrayCompanionEnabled));
                    }

                    if (autoShowChanged &&
                        _companionSettings.AutoShowMatchOnGameStart)
                    {
                        TryAutoShowMatch(_snapshot);
                    }
                });
            }
        }

        private void TryAutoShowMatch(LiveMatchSnapshot snapshot)
        {
            snapshot ??= LiveMatchSnapshot.Empty;
            if (ShouldResetAutoShowFallback(snapshot.GameflowPhase))
            {
                _autoShowHandledWithoutGameId = false;
            }

            if (!_companionSettings.AutoShowMatchOnGameStart ||
                snapshot.GameflowPhase != GameflowPhase.InProgress)
            {
                return;
            }

            var roster = snapshot.Roster;
            if ((roster?.MyTeam?.Count ?? 0) == 0 ||
                (roster?.TheirTeam?.Count ?? 0) == 0)
            {
                return;
            }

            if (roster.GameId > 0)
            {
                if (_lastAutoShownGameId == roster.GameId)
                {
                    return;
                }

                _lastAutoShownGameId = roster.GameId;
            }
            else
            {
                if (_autoShowHandledWithoutGameId)
                {
                    return;
                }

                _autoShowHandledWithoutGameId = true;
            }

            if (_currentMenu != MenuName.Match)
            {
                Navigate(MenuName.Match);
            }

            _eventAggregator.GetEvent<ShowMainWindowEvent>().Publish();
        }

        private static bool ShouldResetAutoShowFallback(GameflowPhase phase)
        {
            return phase is GameflowPhase.None or
                GameflowPhase.Lobby or
                GameflowPhase.Matchmaking or
                GameflowPhase.ReadyCheck or
                GameflowPhase.ChampSelect or
                GameflowPhase.GameStart or
                GameflowPhase.WaitingForStats or
                GameflowPhase.PreEndOfGame or
                GameflowPhase.EndOfGame or
                GameflowPhase.TerminatedInError;
        }

        private void HandleUpdateStateChanged(object sender, UpdateStateChangedEventArgs args)
        {
            Dispatch(RefreshUpdateState);
        }

        private void RefreshUpdateState()
        {
            HasAvailableUpdate = _updateService.AvailableUpdate is not null;
        }

        private async void ExecuteAcceptReadyCheckFromTray()
        {
            try
            {
                await _matchService.AcceptReadyCheckAsync();
            }
            catch (Exception exception)
            {
                Log.Warning(exception, "Unable to accept ready check from the tray");
            }
        }

        private DelegateCommand CreateQuickMatchCommand(
            int queueId,
            string queueNameResourceKey,
            string operation)
        {
            return new DelegateCommand(
                () => SelectAndCreateQuickMatchLobbyAsync(queueId, queueNameResourceKey)
                    .Observe(operation),
                CanCreateQuickMatchLobby);
        }

        private async Task SelectAndCreateQuickMatchLobbyAsync(
            int queueId,
            string queueNameResourceKey)
        {
            _selectedQuickMatchQueueId = queueId;
            _quickMatchSettings.SaveQueueId(queueId);
            UpdateTrayQuickMatchText();
            await CreateQuickMatchLobbyAsync(queueId, queueNameResourceKey);
        }

        private bool CanCreateQuickMatchLobby()
        {
            return !_isCreatingQuickMatchLobby &&
                   _snapshot.ConnectionState == ConnectionState.Connected &&
                   _snapshot.GameflowPhase is GameflowPhase.None or GameflowPhase.Lobby;
        }

        private async Task CreateQuickMatchLobbyAsync(
            int queueId,
            string queueNameResourceKey)
        {
            var operationId = Guid.NewGuid();
            var stopwatch = Stopwatch.StartNew();
            var connectionState = _snapshot.ConnectionState;
            var gameflowPhase = _snapshot.GameflowPhase;
            var queueName = Text(queueNameResourceKey);

            if (!CanCreateQuickMatchLobby())
            {
                var rejectionMessage = Text("HomePage.QuickMatch.Unavailable");
                WriteQuickMatchOperation(
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

            _isCreatingQuickMatchLobby = true;
            UpdateTrayQuickMatchAvailability();
            _quickMatchLobbyCts = new CancellationTokenSource();
            var level = LogEventLevel.Error;
            var outcome = "Failed";
            var message = Text(gameflowPhase == GameflowPhase.Lobby
                ? "HomePage.QuickMatch.ChangeFailed"
                : "HomePage.QuickMatch.Failed");
            string errorCode = "LobbyNotConfirmed";
            string errorType = null;
            Exception operationException = null;
            try
            {
                var result = await _gameService.CreateMatchmadeLobbyAsync(
                    queueId,
                    _quickMatchLobbyCts.Token);
                switch (result.Status)
                {
                    case MatchmadeLobbyCreationStatus.Created:
                        level = LogEventLevel.Information;
                        outcome = "Succeeded";
                        message = string.Format(
                            Text(gameflowPhase == GameflowPhase.Lobby
                                ? "HomePage.QuickMatch.Changed"
                                : "HomePage.QuickMatch.Created"),
                            queueName);
                        errorCode = null;
                        break;
                    case MatchmadeLobbyCreationStatus.ClientUnavailable:
                        level = LogEventLevel.Warning;
                        outcome = "Rejected";
                        message = Text("HomePage.QuickMatch.Unavailable");
                        errorCode = "ClientUnavailable";
                        break;
                    case MatchmadeLobbyCreationStatus.QueueUnavailable:
                        level = LogEventLevel.Warning;
                        outcome = "Rejected";
                        message = string.Format(
                            Text("HomePage.QuickMatch.QueueUnavailable"), queueName);
                        errorCode = "QueueUnavailable";
                        break;
                    case MatchmadeLobbyCreationStatus.OperationInProgress:
                        level = LogEventLevel.Warning;
                        outcome = "Rejected";
                        message = Text("HomePage.QuickMatch.Unavailable");
                        errorCode = "OperationInProgress";
                        break;
                }
            }
            catch (OperationCanceledException)
            {
                level = LogEventLevel.Information;
                outcome = "Cancelled";
                message = Text("HomePage.QuickMatch.Cancelled");
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
                _quickMatchLobbyCts?.Dispose();
                _quickMatchLobbyCts = null;
                _isCreatingQuickMatchLobby = false;
                UpdateTrayQuickMatchAvailability();
            }

            WriteQuickMatchOperation(
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

        private static void WriteQuickMatchOperation(
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
                gameflowPhase == GameflowPhase.Lobby
                    ? "lobby.matchmade.change"
                    : "lobby.matchmade.create",
                "Lobby",
                "Manual",
                outcome,
                operationId,
                "Tray",
                displayMessage,
                properties,
                exception);
        }

        private void UpdateTrayQuickMatchAvailability()
        {
            IsTrayQuickMatchAvailable = CanCreateQuickMatchLobby();
            _quickStartLastFromTrayCommand?.RaiseCanExecuteChanged();
            _quickStartSoloDuoFromTrayCommand?.RaiseCanExecuteChanged();
            _quickStartFlexFromTrayCommand?.RaiseCanExecuteChanged();
            _quickStartAramFromTrayCommand?.RaiseCanExecuteChanged();
            _quickStartHextechAramFromTrayCommand?.RaiseCanExecuteChanged();
        }

        private void UpdateTrayQuickMatchText()
        {
            var queueName = Text(GetQuickMatchQueueNameResourceKey(
                _selectedQuickMatchQueueId));
            TrayQuickMatchLastText = string.Format(
                Text("HomePage.QuickMatch.Button"), queueName);
        }

        private static string GetQuickMatchQueueNameResourceKey(int queueId)
        {
            return GameModeResolver.Classify(queueId) switch
            {
                GameModeKind.RankedFlex => "HomePage.QuickMatch.Flex",
                GameModeKind.Aram => "HomePage.QuickMatch.Aram",
                GameModeKind.HextechAram => "HomePage.QuickMatch.HextechAram",
                _ => "HomePage.QuickMatch.SoloDuo"
            };
        }

        private static int NormalizeQuickMatchQueueId(int queueId)
        {
            return GameModeResolver.IsQuickMatchQueue(queueId)
                    ? queueId
                    : GameQueueIds.RankedSoloDuo;
        }

        private async Task FlashClientSafelyAsync()
        {
            try
            {
                await _clientService.FlashClient();
            }
            catch (Exception exception)
            {
                Log.Debug(exception, "League client could not be flashed");
            }
        }

        private void Navigate(MenuName menuName, SummonerAccount summoner = null)
        {
            if (!CanNavigate(menuName))
            {
                return;
            }

            EnsureModuleLoaded(menuName);
            var parameters = new NavigationParameters();
            if (menuName is MenuName.Career or MenuName.Search)
            {
                parameters.Add(ParameterNames.Summoner, summoner);
            }

            _regionManager.RequestNavigate(
                RegionNames.ContentRegion,
                menuName.ToString(),
                result =>
                {
                    if (result.Result != true)
                    {
                        return;
                    }

                    _currentMenu = menuName;
                    RaiseNavigationSelectionChanged();
                    UpdateWindowTitle(menuName);
                },
                parameters);
        }

        private void OpenTrayView(MenuName menuName)
        {
            ShowMainWindow();
            Navigate(menuName);
        }

        private static void ShowMainWindow()
        {
            var mainWindow = Application.Current?.MainWindow;
            if (mainWindow is null)
            {
                return;
            }

            if (!mainWindow.IsVisible)
            {
                mainWindow.Show();
            }

            if (mainWindow.WindowState == WindowState.Minimized)
            {
                mainWindow.WindowState = WindowState.Normal;
            }

            mainWindow.Activate();
        }

        private void UpdateTrayState(LiveMatchSnapshot snapshot)
        {
            var wasClientNavigationAvailable = IsClientNavigationAvailable;
            _snapshot = snapshot ?? LiveMatchSnapshot.Empty;
            snapshot = _snapshot;
            if (wasClientNavigationAvailable != IsClientNavigationAvailable)
            {
                RaisePropertyChanged(nameof(IsClientNavigationAvailable));
                RaiseClientNavigationCanExecuteChanged();
            }

            var connectionText = Text(GetConnectionStatusKey(snapshot.ConnectionState));
            TrayClientStatus = string.Format(Text("Tray.ClientStatus"), connectionText);
            TrayGameflowStatus = string.Format(
                Text("Tray.GameflowStatus"),
                GetTrayGameflowText(snapshot));
            TrayToolTip = $"{PrometheusTitle} · {connectionText}";
            IsTrayMatchAvailable = snapshot.ConnectionState == ConnectionState.Connected &&
                                   snapshot.GameflowPhase == GameflowPhase.ChampSelect;
            IsTrayReadyCheckAvailable = snapshot.ConnectionState == ConnectionState.Connected &&
                                        snapshot.GameflowPhase == GameflowPhase.ReadyCheck &&
                                        !string.Equals(
                                            snapshot.ReadyCheck?.PlayerResponse,
                                            "Accepted",
                                            StringComparison.OrdinalIgnoreCase);
            UpdateTrayQuickMatchAvailability();
        }

        private void UpdateTrayLocalizedText()
        {
            TrayQuickMatchText = Text("Tray.QuickMatch");
            TrayQuickMatchSoloDuoText = Text("HomePage.QuickMatch.SoloDuo");
            TrayQuickMatchFlexText = Text("HomePage.QuickMatch.Flex");
            TrayQuickMatchAramText = Text("HomePage.QuickMatch.Aram");
            TrayQuickMatchHextechAramText = Text("HomePage.QuickMatch.HextechAram");
            UpdateTrayQuickMatchText();
            TrayShowMainWindowText = Text("Tray.ShowMainWindow");
            TrayOpenMatchText = Text("Tray.OpenMatch");
            TrayAcceptText = Text("HomePage.Action.Accept");
            TrayAutomationText = Text("Tray.Automation");
            TrayAutoAcceptText = Text("Setting.Automation.AutoAccept");
            TrayAutoReconnectText = Text("Setting.Automation.AutoReconnect");
            TrayAramSwapText = Text("Utility.AramSwap");
            TrayCompanionText = Text("Utility.Companion.Title");
            TraySettingsText = Text("Menu.Setting");
            TrayExitText = Text("Tray.Exit");
        }

        private string GetTrayGameflowText(LiveMatchSnapshot snapshot)
        {
            if (snapshot.ConnectionState != ConnectionState.Connected)
            {
                return Text("Tray.GameflowUnavailable");
            }

            var key = snapshot.GameflowPhase switch
            {
                GameflowPhase.None => "HomePage.Phase.Idle.Title",
                GameflowPhase.Lobby => "HomePage.Phase.Lobby.Title",
                GameflowPhase.Matchmaking => "HomePage.Phase.Matchmaking.Title",
                GameflowPhase.ReadyCheck => "HomePage.Phase.Ready.Title",
                GameflowPhase.ChampSelect => "HomePage.Phase.Champion.Title",
                GameflowPhase.GameStart or GameflowPhase.InProgress => "HomePage.Phase.InGame.Title",
                GameflowPhase.Reconnect => "HomePage.Phase.Reconnect.Title",
                GameflowPhase.WaitingForStats or GameflowPhase.PreEndOfGame or GameflowPhase.EndOfGame =>
                    "HomePage.Phase.PostGameLoading.Title",
                GameflowPhase.TerminatedInError => "HomePage.Phase.Error.Title",
                _ => "HomePage.Phase.Unknown.Title"
            };

            return Text(key);
        }

        private static string GetConnectionStatusKey(ConnectionState connectionState)
        {
            return connectionState switch
            {
                ConnectionState.Connected => "Setting.Connection.Connected",
                ConnectionState.Connecting => "Setting.Connection.Connecting",
                ConnectionState.Reconnecting => "Setting.Connection.Reconnecting",
                ConnectionState.Stopping => "Setting.Connection.Stopping",
                ConnectionState.Error => "Setting.Connection.Error",
                _ => "Setting.Connection.Disconnected"
            };
        }

        private bool CanNavigateToClientFeature()
        {
            return IsClientNavigationAvailable;
        }

        private bool CanNavigate(MenuName menuName)
        {
            return !RequiresClientConnection(menuName) ||
                   IsClientNavigationAvailable;
        }

        private static bool RequiresClientConnection(MenuName menuName)
        {
            return menuName is MenuName.Career or
                MenuName.Inventory or
                MenuName.Search or
                MenuName.Match or
                MenuName.Utility;
        }

        private static bool IsTerminalClientUnavailable(
            ConnectionState connectionState)
        {
            return connectionState is ConnectionState.Disconnected or
                ConnectionState.Error;
        }

        private void RaiseClientNavigationCanExecuteChanged()
        {
            _careerCommand?.RaiseCanExecuteChanged();
            _inventoryCommand?.RaiseCanExecuteChanged();
            _searchCommand?.RaiseCanExecuteChanged();
            _matchCommand?.RaiseCanExecuteChanged();
            _utilityCommand?.RaiseCanExecuteChanged();
        }

        private void EnsureModuleLoaded(MenuName menuName)
        {
            switch (menuName)
            {
                case MenuName.Career:
                    LoadModule<SummonerModule>();
                    break;
                case MenuName.Inventory:
                    LoadModule<InventoryModule>();
                    break;
                case MenuName.Search:
                    LoadModule<SearchModule>();
                    break;
                case MenuName.Match:
                    LoadModule<MatchModule>();
                    break;
                case MenuName.Utility:
                    LoadModule<UtilityModule>();
                    break;
            }
        }

        private void UpdateWindowTitle(MenuName menuName)
        {
            if (menuName == MenuName.Home)
            {
                Title = PrometheusTitle;
                return;
            }

            var name = DisplayKeyAttribute.GetDisplayKey(menuName)?.GetDisplayValue();
            Title = string.IsNullOrWhiteSpace(name)
                ? PrometheusTitle
                : $"{PrometheusTitle} -- {name}";
        }

        private void RaiseNavigationSelectionChanged()
        {
            RaisePropertyChanged(nameof(IsHomeSelected));
            RaisePropertyChanged(nameof(IsCareerSelected));
            RaisePropertyChanged(nameof(IsInventorySelected));
            RaisePropertyChanged(nameof(IsSearchSelected));
            RaisePropertyChanged(nameof(IsMatchSelected));
            RaisePropertyChanged(nameof(IsUtilitySelected));
            RaisePropertyChanged(nameof(IsSettingSelected));
        }

        private void LoadModule<T>() where T : IModule
        {
            if (!_moduleManager.IsModuleInitialized<T>())
            {
                _moduleManager.LoadModule<T>();
            }
        }

        private string Text(string key)
        {
            return _resourceService.FindResource<string>(key);
        }

        private static void Dispatch(Action action,
            DispatcherPriority priority = DispatcherPriority.Normal)
        {
            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher is not null && !dispatcher.CheckAccess())
            {
                dispatcher.BeginInvoke(priority, action);
                return;
            }

            action();
        }
    }
}

using HandyControl.Controls;
using Prism.Commands;
using Prism.Events;
using Prism.Modularity;
using Prism.Mvvm;
using Prism.Regions;
using Prism.Services.Dialogs;
using Prometheus.Core;
using Prometheus.Core.Events;
using Prometheus.Core.Models;
using Prometheus.Core.Mvvm;
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
        private readonly IUpdateService _updateService;
        private readonly IDialogService _dialogService;
        private readonly LatestValueDispatcher<LiveMatchSnapshot> _snapshotDispatcher;
        private bool _updateDialogShown;

        private GameflowPhase _lastFlashedPhase = GameflowPhase.Unknown;
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
            IDialogService dialogService)
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
            _dialogService = dialogService;
            _snapshotDispatcher = new LatestValueDispatcher<LiveMatchSnapshot>(
                action => Dispatch(action, DispatcherPriority.Background),
                ApplySnapshot);

            _matchService.SnapshotChanged += HandleSnapshotChanged;
            _automationSettings.Changed += HandleAutomationSettingsChanged;
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

        private DelegateCommand _loadedCommand;
        public DelegateCommand LoadedCommand =>
            _loadedCommand ??= new DelegateCommand(ExecuteLoadedCommand);

        private async void ExecuteLoadedCommand()
        {
            _ = CheckForUpdatesAfterStartupAsync();
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
                var update = await _updateService.CheckAsync(false);
                if (update is null || _updateDialogShown)
                {
                    return;
                }

                _updateDialogShown = true;
                Dispatch(() => _dialogService.ShowDialog(RegionNames.UpdateDialog, _ =>
                {
                    _updateDialogShown = false;
                }));
            }
            catch (Exception exception)
            {
                Log.Warning(exception, "Unable to present the automatic update check");
            }
        }

        private DelegateCommand _homeCommand;
        public DelegateCommand HomeCommand =>
            _homeCommand ??= new DelegateCommand(() => Navigate(MenuName.Home));

        private DelegateCommand _careerCommand;
        public DelegateCommand CareerCommand =>
            _careerCommand ??= new DelegateCommand(() => Navigate(MenuName.Career));

        private DelegateCommand _inventoryCommand;
        public DelegateCommand InventoryCommand =>
            _inventoryCommand ??= new DelegateCommand(() => Navigate(MenuName.Inventory));

        private DelegateCommand _searchCommand;
        public DelegateCommand SearchCommand =>
            _searchCommand ??= new DelegateCommand(() => Navigate(MenuName.Search));

        private DelegateCommand _matchCommand;
        public DelegateCommand MatchCommand =>
            _matchCommand ??= new DelegateCommand(() => Navigate(MenuName.Match));

        private DelegateCommand _utilityCommand;
        public DelegateCommand UtilityCommand =>
            _utilityCommand ??= new DelegateCommand(() => Navigate(MenuName.Utility));

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

        private void HandleNavigateMenu(MenuName menuName)
        {
            Dispatch(() => Navigate(menuName));
        }

        private void HandleSearchSummoner(SummonerAccount summoner)
        {
            Dispatch(() => Navigate(MenuName.Career, summoner));
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

        private async void HandleWindowClosing()
        {
            _matchService.SnapshotChanged -= HandleSnapshotChanged;
            _automationSettings.Changed -= HandleAutomationSettingsChanged;
            _updateService.StateChanged -= HandleUpdateStateChanged;
            _eventAggregator.GetEvent<NavigateMenuEvent>().Unsubscribe(HandleNavigateMenu);
            _eventAggregator.GetEvent<SearchSummonerEvent>().Unsubscribe(HandleSearchSummoner);
            _eventAggregator.GetEvent<TitleChangeEvent>().Unsubscribe(HandleTitleChange);
            _eventAggregator.GetEvent<LanguageSwitchedEvent>().Unsubscribe(HandleLanguageChanged);
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
            var phase = snapshot.GameflowPhase;
            if (_lastFlashedPhase != phase &&
                phase is GameflowPhase.ReadyCheck or GameflowPhase.ChampSelect)
            {
                _ = FlashClientSafelyAsync();
            }

            _lastFlashedPhase = phase;
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
            EnsureModuleLoaded(menuName);
            var parameters = new NavigationParameters();
            if (menuName == MenuName.Career)
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
        }

        private void UpdateTrayLocalizedText()
        {
            TrayShowMainWindowText = Text("Tray.ShowMainWindow");
            TrayOpenMatchText = Text("Tray.OpenMatch");
            TrayAcceptText = Text("HomePage.Action.Accept");
            TrayAutomationText = Text("Tray.Automation");
            TrayAutoAcceptText = Text("Setting.Automation.AutoAccept");
            TrayAutoReconnectText = Text("Setting.Automation.AutoReconnect");
            TrayAramSwapText = Text("Utility.AramSwap");
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

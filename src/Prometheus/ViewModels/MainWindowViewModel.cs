using Prism.Commands;
using Prism.Events;
using Prism.Modularity;
using Prism.Mvvm;
using Prism.Regions;
using Prometheus.Core;
using Prometheus.Core.Events;
using Prometheus.Core.Models;
using Prometheus.Modules.Inventory;
using Prometheus.Modules.Match;
using Prometheus.Modules.Search;
using Prometheus.Modules.Summoner;
using Prometheus.Modules.Utility;
using Prometheus.Services.Interfaces.Client;
using Serilog;
using System;
using System.Threading.Tasks;
using System.Windows;

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

        private GameflowPhase _lastAlertPhase = GameflowPhase.Unknown;
        private bool? _lastConnected;
        private MenuName _currentMenu = MenuName.Home;

        public MainWindowViewModel(
            IRegionManager regionManager,
            IEventAggregator eventAggregator,
            IModuleManager moduleManager,
            IMatchService matchService,
            IClientService clientService,
            IClientListener clientListener,
            IResourceService resourceService)
        {
            _regionManager = regionManager;
            _eventAggregator = eventAggregator;
            _moduleManager = moduleManager;
            _matchService = matchService;
            _clientService = clientService;
            _clientListener = clientListener;
            _resourceService = resourceService;

            _matchService.SnapshotChanged += HandleSnapshotChanged;
            _eventAggregator.GetEvent<NavigateMenuEvent>().Subscribe(HandleNavigateMenu);
            _eventAggregator.GetEvent<SearchSummonerEvent>().Subscribe(HandleSearchSummoner);
            _eventAggregator.GetEvent<TitleChangeEvent>().Subscribe(HandleTitleChange);
            _eventAggregator.GetEvent<LanguageSwitchedEvent>().Subscribe(HandleLanguageChanged);
            _eventAggregator.GetEvent<WindowClosingEvent>().Subscribe(HandleWindowClosing);
        }

        private string _title = PrometheusTitle;
        public string Title
        {
            get => _title;
            set => SetProperty(ref _title, value);
        }

        private bool _hasGlobalAlert;
        public bool HasGlobalAlert
        {
            get => _hasGlobalAlert;
            set => SetProperty(ref _hasGlobalAlert, value);
        }

        private string _globalAlertText;
        public string GlobalAlertText
        {
            get => _globalAlertText;
            set => SetProperty(ref _globalAlertText, value);
        }

        public bool IsHomeSelected => _currentMenu == MenuName.Home;
        public bool IsCareerSelected => _currentMenu == MenuName.Career;
        public bool IsInventorySelected => _currentMenu == MenuName.Inventory;
        public bool IsSearchSelected => _currentMenu == MenuName.Search;
        public bool IsMatchSelected => _currentMenu == MenuName.Match;
        public bool IsUtilitySelected => _currentMenu == MenuName.Utility;
        public bool IsSettingSelected => _currentMenu == MenuName.Setting;

        private DelegateCommand _loadedCommand;
        public DelegateCommand LoadedCommand =>
            _loadedCommand ??= new DelegateCommand(ExecuteLoadedCommand);

        private async void ExecuteLoadedCommand()
        {
            try
            {
                await _matchService.StartAsync();
            }
            catch (Exception exception)
            {
                Log.Error(exception, "Unable to start live match coordinator");
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

        private DelegateCommand _openAlertCommand;
        public DelegateCommand OpenAlertCommand =>
            _openAlertCommand ??= new DelegateCommand(() =>
            {
                HasGlobalAlert = false;
                Navigate(MenuName.Home);
            });

        private DelegateCommand _dismissAlertCommand;
        public DelegateCommand DismissAlertCommand =>
            _dismissAlertCommand ??= new DelegateCommand(() => HasGlobalAlert = false);

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
            UpdateAlertText(_matchService.Current?.GameflowPhase ?? GameflowPhase.Unknown);
            UpdateWindowTitle(_currentMenu);
        }

        private async void HandleWindowClosing()
        {
            _matchService.SnapshotChanged -= HandleSnapshotChanged;
            _eventAggregator.GetEvent<NavigateMenuEvent>().Unsubscribe(HandleNavigateMenu);
            _eventAggregator.GetEvent<SearchSummonerEvent>().Unsubscribe(HandleSearchSummoner);
            _eventAggregator.GetEvent<TitleChangeEvent>().Unsubscribe(HandleTitleChange);
            _eventAggregator.GetEvent<LanguageSwitchedEvent>().Unsubscribe(HandleLanguageChanged);
            try
            {
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
            Dispatch(() => ApplySnapshot(args.Snapshot));
        }

        private void ApplySnapshot(LiveMatchSnapshot snapshot)
        {
            var connected = snapshot.ConnectionState == ConnectionState.Connected;
            if (_lastConnected != connected)
            {
                _lastConnected = connected;
                _eventAggregator.GetEvent<ConnectLCUEvent>().Publish(connected);
            }

            var phase = snapshot.GameflowPhase;
            if (phase is GameflowPhase.ReadyCheck or GameflowPhase.ChampSelect)
            {
                if (_lastAlertPhase != phase)
                {
                    _lastAlertPhase = phase;
                    HasGlobalAlert = true;
                    UpdateAlertText(phase);
                    _ = FlashClientSafelyAsync();
                }
            }
            else
            {
                _lastAlertPhase = phase;
                HasGlobalAlert = false;
            }
        }

        private void UpdateAlertText(GameflowPhase phase)
        {
            if (phase == GameflowPhase.ReadyCheck)
            {
                GlobalAlertText = Text("HomePage.Alert.Ready");
            }
            else if (phase == GameflowPhase.ChampSelect)
            {
                GlobalAlertText = Text("HomePage.Alert.Champion");
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
    }
}

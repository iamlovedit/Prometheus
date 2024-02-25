using Prism.Commands;
using Prism.Events;
using Prism.Modularity;
using Prism.Mvvm;
using Prism.Regions;
using Prometheus.Core;
using Prometheus.Core.Events;
using Prometheus.Modules.Inventory;
using Prometheus.Modules.Match;
using Prometheus.Modules.Search;
using Prometheus.Modules.Summoner;
using Prometheus.Modules.Utility;
using Prometheus.Services.Interfaces;
using Prometheus.Services.Interfaces.Client;
using Serilog;
using System;

namespace Prometheus.ViewModels
{
    public class MainWindowViewModel : BindableBase
    {
        private string _token;
        private string _port;
        private readonly IRegionManager _regionManager;
        private readonly IProcessService _processService;
        private readonly IEventAggregator _eventAggregator;
        private readonly IClientListener _clientListener;
        private readonly IHttpService _httpService;
        private readonly IModuleManager _moduleManager;
        public MainWindowViewModel(IRegionManager regionManager, IProcessService processService, IEventAggregator eventAggregator,
            IClientListener clientListener, IHttpService httpService, IModuleManager moduleManager)
        {
            _regionManager = regionManager;
            _processService = processService;
            _eventAggregator = eventAggregator;
            _clientListener = clientListener;
            _httpService = httpService;
            _moduleManager = moduleManager;
            _moduleManager.LoadModuleCompleted += LoadModuleCompleted;

            _eventAggregator.GetEvent<WindowClosingEvent>().Subscribe(_clientListener.Close);
            _clientListener.OnConnected += () =>
            {
                eventAggregator.GetEvent<ConnectLCUEvent>().Publish(true);
            };
            _clientListener.OnDisconnected += () =>
            {
                eventAggregator.GetEvent<ConnectLCUEvent>().Publish(false);
            };
        }

        private void LoadModuleCompleted(object sender, LoadModuleCompletedEventArgs e)
        {

        }

        private string _title = "Prometheus";
        public string Title
        {
            get { return _title; }
            set { SetProperty(ref _title, value); }
        }

        private DelegateCommand _loadedCommand;
        public DelegateCommand LoadedCommand =>
            _loadedCommand ?? (_loadedCommand = new DelegateCommand(ExecuteLoadedCommand));
        async void ExecuteLoadedCommand()
        {
            _eventAggregator.GetEvent<TitleChangeEvent>().Subscribe(v =>
            {
                Title = "Prometheus--" + v;
            });
            var argsMap = _processService.GetProcessCommandLines();
            if (argsMap != null)
            {
                if (argsMap.TryGetValue("--app-port", out var port) && argsMap.TryGetValue("--remoting-auth-token", out var token))
                {
                    _port = port;
                    _token = token;
                    Log.Information($"port: {port}，token： {token}");
                    _clientListener.Initialize(port, token);
                    _httpService.Initialize(Convert.ToInt32(port), token);
                    await _clientListener.ConnectAsync();
                }
            }
        }

        private DelegateCommand _homeCommand;
        public DelegateCommand HomeCommand =>
            _homeCommand ?? (_homeCommand = new DelegateCommand(ExecuteHomeCommand));
        void ExecuteHomeCommand()
        {
            _regionManager.RequestNavigate(RegionNames.ContentRegion, RegionNames.HomeView);
        }

        private DelegateCommand _careerCommand;
        public DelegateCommand CareerCommand =>
            _careerCommand ?? (_careerCommand = new DelegateCommand(ExecuteCareerCommand));
        void ExecuteCareerCommand()
        {
            LoadModule<SummonerModule>();
            _regionManager.RequestNavigate(RegionNames.ContentRegion, RegionNames.CareerView);
        }

        private DelegateCommand _inventoryComamnd;
        public DelegateCommand InventoryCommand =>
            _inventoryComamnd ?? (_inventoryComamnd = new DelegateCommand(ExecuteInventoryCommand));
        void ExecuteInventoryCommand()
        {
            LoadModule<InventoryModule>();
            _regionManager.RequestNavigate(RegionNames.ContentRegion, RegionNames.InventoryView);
        }

        private DelegateCommand _searchCommand;
        public DelegateCommand SearchCommand =>
            _searchCommand ?? (_searchCommand = new DelegateCommand(ExecuteSearchCommand));
        void ExecuteSearchCommand()
        {
            LoadModule<SearchModule>();
            _regionManager.RequestNavigate(RegionNames.ContentRegion, RegionNames.SearchView);
        }

        private DelegateCommand _utilityCommand;
        public DelegateCommand UtilityCommand =>
            _utilityCommand ?? (_utilityCommand = new DelegateCommand(ExecuteUtilityCommand));
        void ExecuteUtilityCommand()
        {
            LoadModule<UtilityModule>();
            _regionManager.RequestNavigate(RegionNames.ContentRegion, RegionNames.UtilityView);
        }

        private DelegateCommand _matchCommand;
        public DelegateCommand MatchCommand =>
            _matchCommand ?? (_matchCommand = new DelegateCommand(ExecuteMatchCommand));
        void ExecuteMatchCommand()
        {
            LoadModule<MatchModule>();
            _regionManager.RequestNavigate(RegionNames.ContentRegion, RegionNames.MatchView);
        }


        private DelegateCommand _settingCommand;
        public DelegateCommand SettingCommand =>
            _settingCommand ?? (_settingCommand = new DelegateCommand(ExecuteSettingCommand));
        void ExecuteSettingCommand()
        {
            _regionManager.RequestNavigate(RegionNames.ContentRegion, RegionNames.SettingView);
        }


        private void LoadModule<T>() where T : IModule
        {
            if (!_moduleManager.IsModuleInitialized<T>())
            {
                _moduleManager.LoadModule<T>();
            }
        }
    }
}

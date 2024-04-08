using Prism.Commands;
using Prism.Events;
using Prism.Ioc;
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
using Prometheus.Services.Interfaces;
using Prometheus.Services.Interfaces.Client;
using Serilog;
using System;

namespace Prometheus.ViewModels
{
    public class MainWindowViewModel : BindableBase
    {
        private static readonly string _gameflowEvent = "/lol-gameflow/v1/gameflow-phase";
        private static readonly string _prometheus = "Prometheus";
        private string _token;
        private string _port;
        private bool _connected;
        private SummonerAccount _summoner = null;
        private readonly IRegionManager _regionManager;
        private readonly IEventAggregator _eventAggregator;
        private readonly IContainerExtension _containerExtension;
        private readonly IClientListener _clientListener;
        private readonly IHttpService _httpService;
        private readonly IModuleManager _moduleManager;
        private readonly ILeagueClient _leagueClient;

        public MainWindowViewModel(IRegionManager regionManager, IEventAggregator eventAggregator,
            IContainerExtension containerExtension, IModuleManager moduleManager)
        {
            _moduleManager = moduleManager;
            _regionManager = regionManager;
            _eventAggregator = eventAggregator;
            _containerExtension = containerExtension;
            _clientListener = _containerExtension.Resolve<IClientListener>();
            _httpService = _containerExtension.Resolve<IHttpService>();
            _leagueClient = _containerExtension.Resolve<ILeagueClient>();
            _moduleManager.LoadModuleCompleted += LoadModuleCompleted;
            _leagueClient.OnConnected += HandleConnected;
            _leagueClient.OnDisconnected += HandleConnected;
            _leagueClient.Subscribe(_gameflowEvent, HandleGameflowPhase);
            _eventAggregator.GetEvent<WindowClosingEvent>().Subscribe(() =>
            {
                _clientListener.Close();
                _leagueClient.OnConnected -= HandleConnected;
                _leagueClient.OnDisconnected -= HandleDisConnected;
                _leagueClient.Unsubscribe(_gameflowEvent, HandleGameflowPhase);
            });

            _eventAggregator.GetEvent<MatchStartEvent>().Subscribe(started =>
            {
                //TODO:
                if (started)
                {
                    _matchCommand?.Execute();
                }
                else
                {
                    _careerCommand?.Execute();
                }
            });

            _eventAggregator.GetEvent<SearchSummonerEvent>().Subscribe(summoner =>
            {
                _summoner = summoner;
                _careerCommand?.Execute();
            });
        }

        private void HandleConnected()
        {
            _eventAggregator.GetEvent<ConnectLCUEvent>().Publish(true);
        }

        private void HandleDisConnected()
        {
            _eventAggregator.GetEvent<ConnectLCUEvent>().Publish(false);
        }

        private void HandleGameflowPhase(OnWebsocketEventArgs args)
        {
            Console.WriteLine(args.Data);
        }


        private void LoadModuleCompleted(object sender, LoadModuleCompletedEventArgs e)
        {
        }

        private string _title = _prometheus;

        public string Title
        {
            get { return _title; }
            set { SetProperty(ref _title, value); }
        }

        private DelegateCommand _loadedCommand;

        public DelegateCommand LoadedCommand =>
            _loadedCommand ??= new DelegateCommand(ExecuteLoadedCommand);

        async void ExecuteLoadedCommand()
        {
            _eventAggregator.GetEvent<TitleChangeEvent>().Subscribe(v =>
            {
                if (!string.IsNullOrEmpty(v))
                {
                    Title = $"{_prometheus} -- {v}";
                }
                else
                {
                    Title = _prometheus;
                }
            });
            if (await _leagueClient.StartAsync())
            {
                _port = _leagueClient.Port;
                _token = _leagueClient.Token;
                Log.Information($"port: {_port}，token： {_token}");
                _httpService.Initialize(Convert.ToInt32(_port), _token);
            }
        }

        private DelegateCommand _homeCommand;

        public DelegateCommand HomeCommand =>
            _homeCommand ??= new DelegateCommand(ExecuteHomeCommand);

        void ExecuteHomeCommand()
        {
            _regionManager.RequestNavigate(RegionNames.ContentRegion, MenuName.Home.ToString(), result =>
            {
                if (result.Result ?? false)
                {
                    _eventAggregator.GetEvent<TitleChangeEvent>().Publish(string.Empty);
                }
            });
        }

        private DelegateCommand _careerCommand;

        public DelegateCommand CareerCommand =>
            _careerCommand ??= new DelegateCommand(ExecuteCareerCommand);

        void ExecuteCareerCommand()
        {
            LoadModule<SummonerModule>();

            var parameters = new NavigationParameters()
            {
                { ParameterNames.Summoner, _summoner }
            };
            _regionManager.RequestNavigate(RegionNames.ContentRegion, MenuName.Career.ToString(), result =>
            {
                if (result.Result ?? false)
                {
                    var name = DisplayKeyAttribute.GetDisplayKey(MenuName.Career)?.GetDisplayValue();
                    _eventAggregator.GetEvent<TitleChangeEvent>().Publish(name);
                }
            }, parameters);
        }

        private DelegateCommand _inventoryCommand;

        public DelegateCommand InventoryCommand =>
            _inventoryCommand ??= new DelegateCommand(ExecuteInventoryCommand);

        void ExecuteInventoryCommand()
        {
            LoadModule<InventoryModule>();
            Navigate(MenuName.Inventory);
        }

        private DelegateCommand _searchCommand;

        public DelegateCommand SearchCommand =>
            _searchCommand ??= new DelegateCommand(ExecuteSearchCommand);

        void ExecuteSearchCommand()
        {
            LoadModule<SearchModule>();
            Navigate(MenuName.Search);
        }

        private DelegateCommand _utilityCommand;

        public DelegateCommand UtilityCommand =>
            _utilityCommand ??= new DelegateCommand(ExecuteUtilityCommand);

        void ExecuteUtilityCommand()
        {
            LoadModule<UtilityModule>();
            Navigate(MenuName.Utility);
        }

        private DelegateCommand _matchCommand;

        public DelegateCommand MatchCommand =>
            _matchCommand ??= new DelegateCommand(ExecuteMatchCommand);

        void ExecuteMatchCommand()
        {
            LoadModule<MatchModule>();
            Navigate(MenuName.Match);
        }


        private DelegateCommand _settingCommand;

        public DelegateCommand SettingCommand =>
            _settingCommand ??= new DelegateCommand(ExecuteSettingCommand);

        void ExecuteSettingCommand()
        {
            Navigate(MenuName.Setting);
        }

        private void Navigate(MenuName menuName)
        {
            _regionManager.RequestNavigate(RegionNames.ContentRegion, menuName.ToString(), result =>
            {
                if (result.Result ?? false)
                {
                    var name = DisplayKeyAttribute.GetDisplayKey(menuName)?.GetDisplayValue();
                    _eventAggregator.GetEvent<TitleChangeEvent>().Publish(name);
                }
            });
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
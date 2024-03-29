﻿using Prism.Commands;
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
        private readonly IClientService _clientService;
        private readonly IGameResourceManager _gameResourceManager;
        public MainWindowViewModel(IRegionManager regionManager, IEventAggregator eventAggregator, IContainerExtension containerExtension, IModuleManager moduleManager)
        {
            _moduleManager = moduleManager;
            _regionManager = regionManager;
            _eventAggregator = eventAggregator;
            _containerExtension = containerExtension;
            _clientService = _containerExtension.Resolve<IClientService>();
            _clientListener = _containerExtension.Resolve<IClientListener>();
            _httpService = _containerExtension.Resolve<IHttpService>();
            _gameResourceManager = _containerExtension.Resolve<IGameResourceManager>();

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
            _loadedCommand ?? (_loadedCommand = new DelegateCommand(ExecuteLoadedCommand));
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
            var argsMap = _clientService.GetClientCommandLines();
            if (argsMap != null)
            {
                if (argsMap.TryGetValue("--app-port", out var port) && argsMap.TryGetValue("--remoting-auth-token", out var token))
                {
                    _port = port;
                    _token = token;
                    _connected = true;
                    Log.Information($"port: {port}，token： {token}");
                    _httpService.Initialize(Convert.ToInt32(port), token);
                    _clientListener.Initialize(port, token);
                    await _clientListener.ConnectAsync();
                }
            }
        }
        private DelegateCommand _homeCommand;
        public DelegateCommand HomeCommand =>
            _homeCommand ?? (_homeCommand = new DelegateCommand(ExecuteHomeCommand));
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
            _careerCommand ?? (_careerCommand = new DelegateCommand(ExecuteCareerCommand));
        void ExecuteCareerCommand()
        {
            LoadModule<SummonerModule>();

            var parameters = new NavigationParameters()
            {
                {ParameterNames.Summoner,_summoner }
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

        private DelegateCommand _inventoryComamnd;
        public DelegateCommand InventoryCommand =>
            _inventoryComamnd ?? (_inventoryComamnd = new DelegateCommand(ExecuteInventoryCommand));
        void ExecuteInventoryCommand()
        {
            LoadModule<InventoryModule>();
            Navigate(MenuName.Inventory);
        }

        private DelegateCommand _searchCommand;
        public DelegateCommand SearchCommand =>
            _searchCommand ?? (_searchCommand = new DelegateCommand(ExecuteSearchCommand));
        void ExecuteSearchCommand()
        {
            LoadModule<SearchModule>();
            Navigate(MenuName.Search);
        }

        private DelegateCommand _utilityCommand;
        public DelegateCommand UtilityCommand =>
            _utilityCommand ?? (_utilityCommand = new DelegateCommand(ExecuteUtilityCommand));
        void ExecuteUtilityCommand()
        {
            LoadModule<UtilityModule>();
            Navigate(MenuName.Utility);
        }

        private DelegateCommand _matchCommand;
        public DelegateCommand MatchCommand =>
            _matchCommand ?? (_matchCommand = new DelegateCommand(ExecuteMatchCommand));
        void ExecuteMatchCommand()
        {
            LoadModule<MatchModule>();
            Navigate(MenuName.Match);
        }


        private DelegateCommand _settingCommand;
        public DelegateCommand SettingCommand =>
            _settingCommand ?? (_settingCommand = new DelegateCommand(ExecuteSettingCommand));
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

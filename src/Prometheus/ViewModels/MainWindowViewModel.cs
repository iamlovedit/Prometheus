﻿using Prism.Commands;
using Prism.Events;
using Prism.Ioc;
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
using Prometheus.Services.Client;
using Prometheus.Services.Interfaces;
using Prometheus.Services.Interfaces.Client;
using Serilog;
using System;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;

namespace Prometheus.ViewModels
{
    public class MainWindowViewModel : BindableBase
    {
        private string _token;
        private string _port;
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
            var argsMap = _clientService.GetClientCommandLines();
            if (argsMap != null)
            {
                if (argsMap.TryGetValue("--app-port", out var port) && argsMap.TryGetValue("--remoting-auth-token", out var token))
                {
                    _port = port;
                    _token = token;
                    Log.Information($"port: {port}，token： {token}");
                    _clientListener.Initialize(port, token);
                    _httpService.Initialize(Convert.ToInt32(port), token);
                    InitializeResource();
                    await _clientListener.ConnectAsync();
                }
            }
        }

        private async Task InitializeResource()
        {
            Task.Run(async () =>
              {
                  var equipments = await _gameResourceManager.GetEquipmentsAsync();
                  Task.Run(async () =>
                  {
                      var dir = _containerExtension.Resolve<string>(ParameterNames.Equipments);
                      foreach (var equipment in equipments)
                      {
                          if (equipment.InStore)
                          {
                              DownloadFileAsync(equipment.IconPath, dir, $"{equipment.Id}.png");
                          }
                      }
                  });
                  var skinsMap = await _gameResourceManager.GetSkinsAsync();
                  Task.Run(async () =>
                  {
                      var dir = _containerExtension.Resolve<string>(ParameterNames.Skins);
                      foreach (var skinPair in skinsMap)
                      {
                          DownloadFileAsync(skinPair.Value.SplashPath, dir, $"{skinPair.Key}.jpg");
                      }
                  });
                  var champions = await _gameResourceManager.GetChampionSummarysAsync();
                  Task.Run(async () =>
                  {
                      var dir = _containerExtension.Resolve<string>(ParameterNames.ChampoinIcon);
                      foreach (var champion in champions)
                      {
                          DownloadFileAsync(champion.SquarePortraitPath, dir, $"{champion.Id}.png");
                      }
                  });
                  var perks = await _gameResourceManager.GetPerksAsync();
                  Task.Run(async () =>
                  {
                      var dir = _containerExtension.Resolve<string>(ParameterNames.Perks);
                      foreach (var perk in perks)
                      {
                          DownloadFileAsync(perk.IconPath, dir, $"{perk.Id}.png");
                      }
                  });

                  var spells = await _gameResourceManager.GetSpellsAsync();
                  Task.Run(async () =>
                    {
                        var dir = _containerExtension.Resolve<string>(ParameterNames.Spells);
                        foreach (var spell in spells)
                        {
                            DownloadFileAsync(spell.IconPath, dir, $"{spell.Id}.png");
                        }
                    });
              });
        }
        private async Task DownloadFileAsync(string url, string dir, string fileName)
        {
            var fileBuffer = await _httpService.GetByteArrayResponseAsync(HttpMethod.Get, url);
            var filePath = Path.Combine(dir, fileName);
            await File.WriteAllBytesAsync(filePath, fileBuffer);
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

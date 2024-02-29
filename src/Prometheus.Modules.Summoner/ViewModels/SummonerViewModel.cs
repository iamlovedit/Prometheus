using Newtonsoft.Json.Linq;
using Prism.Commands;
using Prism.Events;
using Prism.Ioc;
using Prism.Regions;
using Prism.Services.Dialogs;
using Prometheus.Core;
using Prometheus.Core.Events;
using Prometheus.Core.Models;
using Prometheus.Core.Mvvm;
using Prometheus.Services.Interfaces.Client;
using System.Collections.Generic;
using System.Linq;

namespace Prometheus.Modules.Summoner.ViewModels
{
    public class SummonerViewModel : RegionViewModelBase
    {
        private readonly ISummonerService _summonerService;
        private readonly IGameResourceManager _gameResourceManager;
        private readonly IEventAggregator _eventAggregator;
        private readonly IDialogService _dialogService;
        private readonly IResourceService _resourceService;
        private readonly static Dictionary<int, (string, string)> _mapsMap = new()
        {
            {-1,("特殊地图","Special map") },
            {11,("召唤师峡谷","Summoner's Rift") },
            {12,("嚎哭深渊","Howling Abyss") },
            {21,("极限闪击","Nexus Blitz") },
            {30,("斗魂竞技场","Arena") },
        };

        public SummonerViewModel(IRegionManager regionManager, IEventAggregator eventAggregator, IContainerExtension containerExtension) : base(regionManager)
        {
            _eventAggregator = eventAggregator;
            _eventAggregator.GetEvent<ConnectLCUEvent>().Subscribe(OnConnectHandler);
            _resourceService = containerExtension.Resolve<IResourceService>();
            _summonerService = containerExtension.Resolve<ISummonerService>();
            _gameResourceManager = containerExtension.Resolve<IGameResourceManager>();
            _dialogService = containerExtension.Resolve<IDialogService>();
        }

        private void OnConnectHandler(bool isConnected)
        {
            if (isConnected)
            {
                //TODO:UpDate Summoner 
            }
            else
            {
                //TODO:Clear Summoner
                RegionManager.RequestNavigate(RegionNames.ContentRegion, RegionNames.HomeView);
            }
        }

        public override async void OnNavigatedTo(NavigationContext navigationContext)
        {
            var summoner = await _summonerService.GetCurrentSummoner();
            if (summoner != null)
            {
                var parameters = new NavigationParameters
                {
                    {ParameterNames.Summoner,summoner},
                    {ParameterNames.CanEdit,true}
                };
                RegionManager.RequestNavigate(RegionNames.SummonerContent, RegionNames.SummonerDetailView, parameters);
            }
        }

        void ExecuteModifyCommand()
        {
            _dialogService.ShowDialog(RegionNames.SelectBackgroundDialog, async dialogResult =>
            {
                if (dialogResult.Result == ButtonResult.OK)
                {
                    await _gameResourceManager.SetBackgroundSkinId(0);
                }
            });
        }
    }
}

using Newtonsoft.Json.Linq;
using Prism.Events;
using Prism.Regions;
using Prometheus.Core;
using Prometheus.Core.Events;
using Prometheus.Core.Models;
using Prometheus.Core.Mvvm;
using Prometheus.Services.Interfaces.Client;

namespace Prometheus.Modules.Summoner.ViewModels
{
    public class SummonerViewModel : RegionViewModelBase
    {
        private readonly ISummonerService _summonerService;
        private readonly IGameResourceManager _gameResourceManager;
        private readonly IEventAggregator _eventAggregator;
        public SummonerViewModel(IRegionManager regionManager, IEventAggregator eventAggregator, ISummonerService summonerService, IGameResourceManager gameResourceManager) : base(regionManager)
        {
            _eventAggregator = eventAggregator;
            _eventAggregator.GetEvent<ConnectLCUEvent>().Subscribe(OnConnectHandler);
            _summonerService = summonerService;
            _gameResourceManager = gameResourceManager;
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
            Summoner = await _summonerService.GetCurrentSummoner();
            if (Summoner != null)
            {
                var jsonValue = await _gameResourceManager.GetBackgroundSkinId();
                if (!string.IsNullOrEmpty(jsonValue))
                {
                    var skinId = JObject.Parse(jsonValue)["backgroundSkinId"].ToObject<int>();
                }
            }
        }

        private SummonerAccount _summoner;
        public SummonerAccount Summoner
        {
            get { return _summoner; }
            set { SetProperty(ref _summoner, value); }
        }
    }
}

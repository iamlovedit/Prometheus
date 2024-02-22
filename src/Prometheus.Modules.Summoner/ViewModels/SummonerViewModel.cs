using Newtonsoft.Json;
using Prism.Events;
using Prism.Regions;
using Prometheus.Core;
using Prometheus.Core.Events;
using Prometheus.Core.Mvvm;
using Prometheus.Services.Interfaces.Client;
using Prometheus.Services.Interfaces.Models;

namespace Prometheus.Modules.Summoner.ViewModels
{
    public class SummonerViewModel : RegionViewModelBase
    {
        private readonly ISummonerService _summonerService;

        public SummonerViewModel(IRegionManager regionManager, IEventAggregator eventAggregator, ISummonerService summonerService) : base(regionManager)
        {
            eventAggregator.GetEvent<ConnectLCUEvent>().Subscribe(OnConnectHandler);
            eventAggregator.GetEvent<ConnectLCUEvent>().Subscribe(OnConnectHandler);
            _summonerService = summonerService;
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
            var result = await _summonerService.GetCurrentSummoner();
            var account = JsonConvert.DeserializeObject<SummonerAccount>(result);
        }
    }
}

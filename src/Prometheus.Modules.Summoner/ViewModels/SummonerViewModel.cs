using Prism.Events;
using Prism.Regions;
using Prometheus.Core;
using Prometheus.Core.Events;
using Prometheus.Core.Mvvm;

namespace Prometheus.Modules.Summoner.ViewModels
{
    public class SummonerViewModel : RegionViewModelBase
    {
        public SummonerViewModel(IRegionManager regionManager, IEventAggregator eventAggregator) : base(regionManager)
        {
            eventAggregator.GetEvent<ConnectLCUEvent>().Subscribe(OnConnectHandler);
            eventAggregator.GetEvent<ConnectLCUEvent>().Subscribe(OnConnectHandler);
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
    }
}

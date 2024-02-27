using Prism.Ioc;
using Prism.Modularity;
using Prometheus.Core;
using Prometheus.Modules.Summoner.Views;

namespace Prometheus.Modules.Summoner
{
    public class SummonerModule : IModule
    {
        public void OnInitialized(IContainerProvider containerProvider)
        {

        }

        public void RegisterTypes(IContainerRegistry containerRegistry)
        {
            containerRegistry.RegisterForNavigation<SummonerView>(RegionNames.CareerView);
            containerRegistry.RegisterDialog<SelectBackgroundDialog>(RegionNames.SelectBackgroundDialog);
        }
    }
}
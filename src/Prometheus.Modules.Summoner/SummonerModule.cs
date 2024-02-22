using Prism.Ioc;
using Prism.Modularity;
using Prometheus.Core;
using Prometheus.Modules.Summoner.Views;
using Prometheus.Services.Client;
using Prometheus.Services.Interfaces.Client;

namespace Prometheus.Modules.Summoner
{
    public class SummonerModule : IModule
    {
        public void OnInitialized(IContainerProvider containerProvider)
        {

        }

        public void RegisterTypes(IContainerRegistry containerRegistry)
        {
            containerRegistry.RegisterSingleton<ISummonerService, SummonerService>();
            containerRegistry.RegisterForNavigation<SummonerView>(RegionNames.CareerView);
        }
    }
}
using Prism.Ioc;
using Prism.Modularity;
using Prometheus.Core;
using Prometheus.Modules.Summoner.Views;
using Prometheus.Services;
using Prometheus.Services.Interfaces;

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
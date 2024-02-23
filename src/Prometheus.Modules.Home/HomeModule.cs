using Prism.Ioc;
using Prism.Regions;
using Prometheus.Core;
using Prometheus.Core.Mvvm;
using Prometheus.Modules.Home.Views;

namespace Prometheus.Modules.Home
{
    public class HomeModule : ModuleBase
    {
        public HomeModule(IRegionManager regionManager) : base(regionManager)
        {
        }

        public override void OnInitialized(IContainerProvider containerProvider)
        {
            RegionManager.RequestNavigate(RegionNames.ContentRegion, RegionNames.HomeView);
        }

        public override void RegisterTypes(IContainerRegistry containerRegistry)
        {
            containerRegistry.RegisterForNavigation<HomeView>(RegionNames.HomeView);

        }
    }
}
using Prism.Ioc;
using Prism.Regions;
using Prometheus.Core;
using Prometheus.Core.Mvvm;
using Prometheus.Modules.Search.Views;

namespace Prometheus.Modules.Search
{
    public class SearchModule : ModuleBase
    {
        public SearchModule(IRegionManager regionManager) : base(regionManager)
        {
        }

        public override void OnInitialized(IContainerProvider containerProvider)
        {

        }

        public override void RegisterTypes(IContainerRegistry containerRegistry)
        {
            containerRegistry.RegisterForNavigation<SearchView>(RegionNames.SearchView);
            containerRegistry.RegisterForNavigation<NotFoundView>(RegionNames.UserNotFoundView);
        }
    }
}
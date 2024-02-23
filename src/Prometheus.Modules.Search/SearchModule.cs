using Prism.Ioc;
using Prism.Modularity;
using Prism.Regions;
using Prometheus.Core;
using Prometheus.Modules.Search.Views;

namespace Prometheus.Modules.Search
{
    public class SearchModule : IModule
    {
        public void OnInitialized(IContainerProvider containerProvider)
        {
        }

        public void RegisterTypes(IContainerRegistry containerRegistry)
        {
            containerRegistry.RegisterForNavigation<SearchView>(RegionNames.SearchView);
            containerRegistry.RegisterForNavigation<NotFoundView>(RegionNames.UserNotFoundView);
        }
    }
}
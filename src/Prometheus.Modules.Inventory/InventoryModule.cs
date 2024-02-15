using Prism.Ioc;
using Prism.Modularity;
using Prism.Regions;
using Prometheus.Core;
using Prometheus.Modules.Inventory.Views;

namespace Prometheus.Modules.Inventory
{
    public class InventoryModule : IModule
    {
        public void OnInitialized(IContainerProvider containerProvider)
        {

        }

        public void RegisterTypes(IContainerRegistry containerRegistry)
        {
            containerRegistry.RegisterForNavigation<InventoryView>(RegionNames.InventoryView);
        }
    }
}
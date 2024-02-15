using Prism.Ioc;
using Prism.Modularity;
using Prism.Regions;

namespace Prometheus.Core.Mvvm
{
    public abstract class ModuleBase : IModule
    {
        protected IRegionManager RegionManager { get; set; }

        public ModuleBase(IRegionManager regionManager)
        {
            RegionManager = regionManager;
        }

        public abstract void OnInitialized(IContainerProvider containerProvider);

        public abstract void RegisterTypes(IContainerRegistry containerRegistry);
    }
}

using Prism.Ioc;
using Prism.Modularity;
using Prism.Regions;
using Prometheus.Core;
using Prometheus.Core.Mvvm;
using Prometheus.Modules.ModuleName.Views;

namespace Prometheus.Modules.ModuleName
{
    public class ModuleNameModule : ModuleBase
    {
        public ModuleNameModule(IRegionManager regionManager) : base(regionManager)
        {
        }

        public override void OnInitialized(IContainerProvider containerProvider)
        {
            RegionManager.RequestNavigate(RegionNames.ContentRegion, "ViewA");
        }

        public override void RegisterTypes(IContainerRegistry containerRegistry)
        {
            containerRegistry.RegisterForNavigation<ViewA>();
        }
    }
}
using Prism.Ioc;
using Prism.Regions;
using Prometheus.Core;
using Prometheus.Core.Mvvm;
using Prometheus.Modules.Setting.Views;

namespace Prometheus.Modules.Setting
{
    public class SettingModule : ModuleBase
    {
        public SettingModule(IRegionManager regionManager) : base(regionManager)
        {
        }

        public override void OnInitialized(IContainerProvider containerProvider)
        {
            RegionManager.RegisterViewWithRegion(RegionNames.SettingTabRegion, RegionNames.SettingGenericView);
            RegionManager.RegisterViewWithRegion(RegionNames.SettingTabRegion, RegionNames.SettingPreferenceView);
            RegionManager.RegisterViewWithRegion(RegionNames.SettingTabRegion, RegionNames.SettingSystemView);
        }

        public override void RegisterTypes(IContainerRegistry containerRegistry)
        {
            containerRegistry.RegisterForNavigation<SettingView>(RegionNames.SettingView);
            containerRegistry.RegisterForNavigation<GenericView>(RegionNames.SettingGenericView);
            containerRegistry.RegisterForNavigation<SystemView>(RegionNames.SettingSystemView);
            containerRegistry.RegisterForNavigation<PreferenceView>(RegionNames.SettingPreferenceView);
        }
    }
}
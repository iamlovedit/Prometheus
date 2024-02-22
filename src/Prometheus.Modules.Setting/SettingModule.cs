using Prism.Ioc;
using Prism.Regions;
using Prometheus.Core;
using Prometheus.Core.Mvvm;
using Prometheus.Modules.Setting.Properties;
using Prometheus.Modules.Setting.Views;
using Prometheus.Services.Interfaces.Client;
using System;
using System.Globalization;

namespace Prometheus.Modules.Setting
{
    public class SettingModule : ModuleBase
    {
        private readonly IResourceService _resourceService;
        public SettingModule(IRegionManager regionManager, IResourceService resourceService) : base(regionManager)
        {
            _resourceService = resourceService;
        }

        public override void OnInitialized(IContainerProvider containerProvider)
        {
            RegionManager.RegisterViewWithRegion(RegionNames.SettingTabRegion, RegionNames.SettingGenericView);
            RegionManager.RegisterViewWithRegion(RegionNames.SettingTabRegion, RegionNames.SettingPreferenceView);
            RegionManager.RegisterViewWithRegion(RegionNames.SettingTabRegion, RegionNames.SettingSystemView);

            var languageIndex = Settings.Default.LanguageIndex;
            if (languageIndex == -1)
            {
                var cultrue = CultureInfo.CurrentCulture.Name;
                languageIndex = cultrue == "zh-CN" ? 0 : 1;
                Settings.Default.LanguageIndex = languageIndex;
                Settings.Default.Save();
            }
            if (languageIndex != 0)
            {
                _resourceService.SwitchLanguage(languageIndex);
            }

            var themeIndex = Settings.Default.ThemeIndex;
            if (themeIndex != 0)
            {
                _resourceService.SwitchTheme(themeIndex);
            }

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
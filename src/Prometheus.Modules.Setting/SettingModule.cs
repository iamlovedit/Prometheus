using Prism.Ioc;
using Prism.Regions;
using Prometheus.Core;
using Prometheus.Core.Models;
using Prometheus.Core.Mvvm;
using Prometheus.Modules.Setting.Properties;
using Prometheus.Modules.Setting.Views;
using Prometheus.Services.Interfaces.Client;
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
            RegionManager.RegisterViewWithRegion(RegionNames.SettingContentRegion, RegionNames.SettingPreferenceView);

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
            var themeMode = ApplicationThemeModeResolver.Normalize(themeIndex);
            if (themeIndex != (int)themeMode)
            {
                themeIndex = (int)themeMode;
                Settings.Default.ThemeIndex = themeIndex;
                Settings.Default.Save();
            }

            _resourceService.SwitchTheme(themeIndex);

        }

        public override void RegisterTypes(IContainerRegistry containerRegistry)
        {
            containerRegistry.RegisterForNavigation<SettingView>(MenuName.Setting.ToString());
            containerRegistry.RegisterForNavigation<PreferenceView>(RegionNames.SettingPreferenceView);
            containerRegistry.RegisterForNavigation<LogView>(RegionNames.SettingLogView);
        }
    }
}

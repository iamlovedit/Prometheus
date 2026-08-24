using Prism.Ioc;
using Prism.Regions;
using Prometheus.Core;
using Prometheus.Core.Models;
using Prometheus.Core.Mvvm;
using Prometheus.Modules.Setting.Properties;
using Prometheus.Modules.Setting.Views;
using Prometheus.Services.Interfaces.Client;
using System.Configuration;
using System.Globalization;

namespace Prometheus.Modules.Setting
{
    public class SettingModule : ModuleBase
    {
        private readonly IResourceService _resourceService;
        private readonly IApplicationPreferenceSettings _preferenceSettings;

        public SettingModule(
            IRegionManager regionManager,
            IResourceService resourceService,
            IApplicationPreferenceSettings preferenceSettings)
            : base(regionManager)
        {
            _resourceService = resourceService;
            _preferenceSettings = preferenceSettings;
        }

        public override void OnInitialized(IContainerProvider containerProvider)
        {
            RegionManager.RegisterViewWithRegion(RegionNames.SettingContentRegion, RegionNames.SettingPreferenceView);

            var languageIndex = _preferenceSettings.LanguageIndex;
            var themeIndex = _preferenceSettings.ThemeIndex;
            var legacySettings = languageIndex.HasValue && themeIndex.HasValue
                ? (-1, -1)
                : LoadLegacySettings();

            if (!languageIndex.HasValue)
            {
                languageIndex = legacySettings.Item1 is >= 0 and <= 1
                    ? legacySettings.Item1
                    : ApplicationPreferenceDefaults.ResolveLanguageIndex(
                        CultureInfo.CurrentUICulture);
                _preferenceSettings.SaveLanguageIndex(languageIndex.Value);
            }

            if (languageIndex.Value != 0)
            {
                _resourceService.SwitchLanguage(languageIndex.Value);
            }

            if (!themeIndex.HasValue)
            {
                themeIndex = Enum.IsDefined((ApplicationThemeMode)legacySettings.Item2)
                    ? legacySettings.Item2
                    : (int)_resourceService.GetSystemThemeMode();
                _preferenceSettings.SaveThemeIndex(themeIndex.Value);
            }
            else
            {
                var themeMode = ApplicationThemeModeResolver.Normalize(themeIndex.Value);
                if (themeIndex != (int)themeMode)
                {
                    themeIndex = (int)themeMode;
                    _preferenceSettings.SaveThemeIndex(themeIndex.Value);
                }
            }

            _resourceService.SwitchTheme(themeIndex.Value);

        }

        public override void RegisterTypes(IContainerRegistry containerRegistry)
        {
            containerRegistry.RegisterForNavigation<SettingView>(MenuName.Setting.ToString());
            containerRegistry.RegisterForNavigation<PreferenceView>(RegionNames.SettingPreferenceView);
            containerRegistry.RegisterForNavigation<LogView>(RegionNames.SettingLogView);
        }

        private static (int LanguageIndex, int ThemeIndex) LoadLegacySettings()
        {
            try
            {
                Settings.Default.Upgrade();
                return (Settings.Default.LanguageIndex, Settings.Default.ThemeIndex);
            }
            catch (ConfigurationErrorsException)
            {
                return (-1, -1);
            }
            catch (IOException)
            {
                return (-1, -1);
            }
            catch (UnauthorizedAccessException)
            {
                return (-1, -1);
            }
        }
    }
}

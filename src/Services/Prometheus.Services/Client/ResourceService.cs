using Microsoft.Win32;
using Prometheus.Core.Models;
using Prometheus.Services.Interfaces.Client;
using Serilog;
using System.Windows;
using System.Windows.Threading;

namespace Prometheus.Services.Client
{
    public class ResourceService : IResourceService, IDisposable
    {
        private const string PersonalizeRegistryPath =
            @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
        private const string AppsUseLightThemeValueName = "AppsUseLightTheme";

        private readonly string _languageUriFormat =
            "pack://application:,,,/Prometheus.Core;component/Resources/Languages/{0}.xaml";
        private readonly string _themeUriFormat =
            "pack://application:,,,/HandyControl;component/Themes/Skin{0}.xaml";
        private readonly string _tierUriFormat =
            "pack://application:,,,/Prometheus.Core;component/Resources/Images/Tiers/{0}.png";

        private int _themeMode = (int)ApplicationThemeMode.Light;
        private int _isDisposed;

        public ResourceService()
        {
            SystemEvents.UserPreferenceChanged += HandleUserPreferenceChanged;
        }

        public T FindResource<T>(string resourceKey)
        {
            return (T)Application.Current.FindResource(resourceKey);
        }

        public string GetLanguageResourceUri(string language)
        {
            return string.Format(_languageUriFormat, language);
        }

        public string GetSkinResourceUri(string theme)
        {
            return string.Format(_themeUriFormat, theme);
        }

        public void SwitchTheme(int themeIndex)
        {
            var themeMode = ApplicationThemeModeResolver.Normalize(themeIndex);
            Volatile.Write(ref _themeMode, (int)themeMode);

            Dispatch(() =>
            {
                if (Volatile.Read(ref _themeMode) == (int)themeMode)
                {
                    ApplyTheme(themeMode);
                }
            });
        }

        public void SwitchLanguage(int languageIndex)
        {
            try
            {
                var language = languageIndex == 0 ? "zh-CN" : "en-US";
                var uri = new Uri(string.Format(_languageUriFormat, language));
                Application.Current.Resources.MergedDictionaries[1]?.MergedDictionaries.Clear();
                Application.Current.Resources.MergedDictionaries[1]?.MergedDictionaries.Add(
                    new ResourceDictionary { Source = uri });
            }
            catch (Exception exception)
            {
                Log.Warning(exception, "Unable to switch application language");
            }
        }

        public string GetTierIconResourceUri(string tier)
        {
            return string.Format(_tierUriFormat, tier);
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _isDisposed, 1) != 0)
            {
                return;
            }

            SystemEvents.UserPreferenceChanged -= HandleUserPreferenceChanged;
            GC.SuppressFinalize(this);
        }

        private void HandleUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs args)
        {
            if (Volatile.Read(ref _themeMode) != (int)ApplicationThemeMode.System)
            {
                return;
            }

            Dispatch(() =>
            {
                if (Volatile.Read(ref _themeMode) == (int)ApplicationThemeMode.System)
                {
                    ApplyTheme(ApplicationThemeMode.System);
                }
            });
        }

        private void ApplyTheme(ApplicationThemeMode themeMode)
        {
            try
            {
                var useDarkTheme = ApplicationThemeModeResolver.ShouldUseDarkTheme(
                    themeMode,
                    SystemUsesLightTheme());
                var targetSkinName = useDarkTheme ? "Dark" : "Default";
                var skinDictionary = new ResourceDictionary
                {
                    Source = new Uri(string.Format(_themeUriFormat, targetSkinName))
                };

                var application = Application.Current;
                if (application?.Resources.MergedDictionaries.Count is not > 0)
                {
                    return;
                }

                var themeContainer = application.Resources.MergedDictionaries[0];
                themeContainer.MergedDictionaries.Clear();
                themeContainer.MergedDictionaries.Add(skinDictionary);
                themeContainer.MergedDictionaries.Add(new ResourceDictionary
                {
                    Source = new Uri(
                        "pack://application:,,,/HandyControl;component/Themes/Theme.xaml")
                });
                application.MainWindow?.OnApplyTemplate();
            }
            catch (Exception exception)
            {
                Log.Warning(exception, "Unable to switch application theme");
            }
        }

        private static bool SystemUsesLightTheme()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(PersonalizeRegistryPath);
                return key?.GetValue(AppsUseLightThemeValueName) is not int value || value != 0;
            }
            catch (Exception exception)
            {
                Log.Debug(exception, "Unable to read the Windows application theme; using light mode");
                return true;
            }
        }

        private static void Dispatch(Action action)
        {
            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher is null || dispatcher.CheckAccess())
            {
                action();
                return;
            }

            dispatcher.BeginInvoke(DispatcherPriority.Background, action);
        }
    }
}

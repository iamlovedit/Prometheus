using Prometheus.Services.Interfaces;
using System;
using System.Windows;

namespace Prometheus.Services
{
    public class ResourceService : IResourceService
    {
        private readonly string _languageUriFormat = "pack://application:,,,/Prometheus.Core;component/Resources/Languages/{0}.xaml";
        private readonly string _themeUriFormat = "pack://application:,,,/HandyControl;component/Themes/Skin{0}.xaml";

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
            try
            {
                var targetSkinName = themeIndex == 0 ? "Default" : "Dark";
                var uri = new Uri(string.Format(_themeUriFormat, targetSkinName));
                Application.Current.Resources.MergedDictionaries[0]?.MergedDictionaries.Clear();
                Application.Current.Resources.MergedDictionaries[0]?.MergedDictionaries.Add(new ResourceDictionary() { Source = uri });
                Application.Current.Resources.MergedDictionaries[0]?.MergedDictionaries.Add(new ResourceDictionary()
                {
                    Source = new Uri("pack://application:,,,/HandyControl;component/Themes/Theme.xaml")
                });
                Application.Current.MainWindow.OnApplyTemplate();
            }
            catch (Exception)
            {

            }

        }

        public void SwitchLanguage(int languageIndex)
        {
            try
            {
                var language = languageIndex == 0 ? "zh-CN" : "en-US";
                var uri = new Uri(string.Format(_languageUriFormat, language));
                Application.Current.Resources.MergedDictionaries[1]?.MergedDictionaries.Clear();
                Application.Current.Resources.MergedDictionaries[1]?.MergedDictionaries.Add(new ResourceDictionary() { Source = uri });
            }
            catch (Exception)
            {

            }
        }
    }
}

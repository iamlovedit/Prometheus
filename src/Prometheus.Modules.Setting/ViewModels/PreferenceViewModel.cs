using HandyControl.Collections;
using Prism.Events;
using Prometheus.Core.Events;
using Prometheus.Services.Interfaces;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;

namespace Prometheus.Modules.Setting.ViewModels
{
    public class PreferenceViewModel : TabItemViewModelBase
    {
        private static readonly string _displayCN = "简体中文";
        private static readonly string _displayUS = "English";
        private static readonly string _keyCN = "zh-CN";
        private static readonly string _keyUS = "en-US";
        private static readonly string _lightCN = "明亮";
        private static readonly string _darkCN = "暗黑";
        private static readonly string _lightUS = "Light";
        private static readonly string _darkUs = "Dark";

        private readonly Dictionary<string, string> _languageMap = new()
        {
            {_displayCN,_keyCN},
            { _displayUS, _keyUS}
        };
        private readonly string[] _themesCN = [_lightCN, _darkCN];

        private readonly string[] _themesUS = [_lightUS, _darkUs];

        protected override string TitleResourceKey { get; set; } = "Setting.Preference";

        public PreferenceViewModel(IEventAggregator eventAggregator, IResourceService resourceService) : base(eventAggregator, resourceService)
        {
            var cultrue = CultureInfo.CurrentCulture.Name;
            if (cultrue == _keyCN)
            {
                _selectedLanguage = _displayCN;
                _themes = _themesCN;
            }
            else
            {
                _selectedLanguage = _displayUS;
                _themes = _themesUS;
            }
            //TODO:get setting from local storage;
            SwitchLanguage(_keyUS, _languageMap[_selectedLanguage]);
        }

        public string[] Languages { get; } = [_displayCN, _displayUS];

        private string _selectedLanguage;
        public string SelectedLanguage
        {
            get { return _selectedLanguage; }
            set
            {
                SwitchLanguage(_languageMap[_selectedLanguage], _languageMap[value]);
                SetProperty(ref _selectedLanguage, value);
            }
        }

        private string[] _themes;
        public string[] Themes
        {
            get { return _themes; }
            set { SetProperty(ref _themes, value); }
        }

        private string _selectedTheme;
        public string SelectedTheme
        {
            get { return _selectedTheme; }
            set { SetProperty(ref _selectedTheme, value); }
        }

        private void SwitchLanguage(string source, string target)
        {
            if (source == target)
            {
                return;
            }
            try
            {
                var sourceUri = new Uri(ResourceService.GetLanguageResourceUri(source));
                var targetUri = new Uri(ResourceService.GetLanguageResourceUri(target));
                ResourceService.RemoveResourceDictionary(sourceUri);
                ResourceService.AddResourceDictionary(targetUri);
                Themes = target == _keyUS ? _themesUS : _themesCN;
            }
            catch (Exception)
            {
                return;
            }
            EventAggregator.GetEvent<LanguageSwitchedEvent>().Publish();
        }
    }
}

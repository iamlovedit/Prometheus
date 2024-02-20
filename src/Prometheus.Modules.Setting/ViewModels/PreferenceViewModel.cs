﻿using HandyControl.Collections;
using Prism.Events;
using Prometheus.Core.Events;
using Prometheus.Services.Interfaces;
using System;
using System.Collections.Generic;
using System.Globalization;

namespace Prometheus.Modules.Setting.ViewModels
{
    public class PreferenceViewModel : TabItemViewModelBase
    {
        private readonly Dictionary<string, string> _languageMap = new()
        {
            {"简体中文","zh-CN" },
            { "English", "en-US" }
        };

        private readonly string[] _themeCN = ["浅色", "暗黑", "跟随系统"];

        private readonly string[] _themeUS = ["Light", "Dark", "System"];

        protected override string TitleResourceKey { get; set; } = "Setting.Preference";

        public PreferenceViewModel(IEventAggregator eventAggregator, IResourceService resourceService) : base(eventAggregator, resourceService)
        {
            var cultrue = CultureInfo.CurrentCulture.Name;
            if (cultrue == "zh-CN")
            {
                _selectedLanguage = "简体中文";
            }
            else
            {
                _selectedLanguage = "English";
            }
            //TODO:get setting from local storage;
            SwitchLanguage("en-US", _languageMap[_selectedLanguage]);
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
            }
            catch (Exception)
            {
                return;
            }
            EventAggregator.GetEvent<LanguageSwitchedEvent>().Publish();
        }
        public string[] Languages { get; } = ["English", "简体中文"];

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

        private ManualObservableCollection<string> _themes;
        public ManualObservableCollection<string> Themes
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

    }
}

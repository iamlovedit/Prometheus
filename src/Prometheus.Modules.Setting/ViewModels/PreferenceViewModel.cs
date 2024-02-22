using Prism.Events;
using Prometheus.Core.Events;
using Prometheus.Modules.Setting.Properties;
using Prometheus.Services.Interfaces.Client;

namespace Prometheus.Modules.Setting.ViewModels
{
    public class PreferenceViewModel : TabItemViewModelBase
    {
        protected override string TitleResourceKey { get; set; } = "Setting.Preference";

        public PreferenceViewModel(IEventAggregator eventAggregator, IResourceService resourceService) : base(eventAggregator, resourceService)
        {

            _selectdLanguageIndex = Settings.Default.LanguageIndex;
            _selectedThemeIndex = Settings.Default.ThemeIndex;

        }

        private int _selectdLanguageIndex;
        public int SelectedLanguageIndex
        {
            get { return _selectdLanguageIndex; }
            set
            {
                SetProperty(ref _selectdLanguageIndex, value);
                ResourceService.SwitchLanguage(value);
                Settings.Default.LanguageIndex = value;
                Settings.Default.Save();
                EventAggregator.GetEvent<LanguageSwitchedEvent>().Publish();
            }
        }


        private int _selectedThemeIndex;
        public int SelectedThemeIndex
        {
            get { return _selectedThemeIndex; }
            set
            {
                SetProperty(ref _selectedThemeIndex, value);
                ResourceService.SwitchTheme(value);
                Settings.Default.ThemeIndex = value;
                Settings.Default.Save();
            }
        }
    }
}

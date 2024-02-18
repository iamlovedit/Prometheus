using Prism.Mvvm;
using Prism.Regions;
using Prometheus.Modules.Setting.Models;

namespace Prometheus.Modules.Setting.ViewModels
{
    public class SettingViewModel : BindableBase
    {
        private readonly IRegionManager _regionManager;
        public SettingViewModel(IRegionManager regionManager)
        {
            _regionManager = regionManager;
        }

        public PreferenceSetting PreferenceSetting { get; set; }

        public GenericSetting GenericSetting { get; set; }

        public SystemSetting SystemSetting { get; set; }
    }
}

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

    }
}

using Prism.Commands;
using Prism.Regions;
using Prometheus.Core;
using Prometheus.Core.Mvvm;

namespace Prometheus.Modules.Setting.ViewModels
{
    public class SettingViewModel : ViewModelBase
    {
        private readonly IRegionManager _regionManager;

        public SettingViewModel(IRegionManager regionManager)
        {
            _regionManager = regionManager;
            OpenOverviewCommand = new DelegateCommand(() =>
                Navigate(RegionNames.SettingPreferenceView, false));
            OpenDiagnosticsCommand = new DelegateCommand(() =>
                Navigate(RegionNames.SettingLogView, true));
        }

        public DelegateCommand OpenOverviewCommand { get; }

        public DelegateCommand OpenDiagnosticsCommand { get; }

        private bool _isDiagnosticsOpen;
        public bool IsDiagnosticsOpen
        {
            get => _isDiagnosticsOpen;
            private set => SetProperty(ref _isDiagnosticsOpen, value);
        }

        private void Navigate(string target, bool isDiagnosticsOpen)
        {
            _regionManager.RequestNavigate(
                RegionNames.SettingContentRegion,
                target,
                result =>
                {
                    if (result.Result == true)
                    {
                        IsDiagnosticsOpen = isDiagnosticsOpen;
                    }
                });
        }
    }
}

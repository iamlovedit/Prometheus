using Prism.Regions;
using Prometheus.Core.Mvvm;
using Prometheus.Services.Interfaces.Client;

namespace Prometheus.Modules.Utility.ViewModels
{
    public class UtilityViewModel : RegionViewModelBase
    {
        private readonly IClientService _clientService;
        public UtilityViewModel(IRegionManager regionManager, IClientService clientService) : base(regionManager)
        {
            _clientService = clientService;
            Initialize();
        }

        private string _installtionPath;
        public string InstalltionPath
        {
            get { return _installtionPath; }
            set { SetProperty(ref _installtionPath, value); }
        }

        private async void Initialize()
        {
            InstalltionPath = await _clientService.GetInstallLocation();
        }
    }
}

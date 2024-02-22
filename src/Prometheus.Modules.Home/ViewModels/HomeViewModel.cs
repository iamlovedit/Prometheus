using Prism.Events;
using Prism.Regions;
using Prometheus.Core.Events;
using Prometheus.Core.Mvvm;
using Prometheus.Services.Interfaces.Client;

namespace Prometheus.Modules.Home.ViewModels
{
    public class HomeViewModel : RegionViewModelBase
    {
        private readonly IEventAggregator _eventAggregator;
        private readonly IResourceService _resourceService;

        public HomeViewModel(IRegionManager regionManager, IEventAggregator eventAggregator, IResourceService resourceService) : base(regionManager)
        {
            _eventAggregator = eventAggregator;
            _resourceService = resourceService;
            _eventAggregator.GetEvent<ConnectLCUEvent>().Subscribe(isConnected =>
            {
                IsConnected = isConnected;
                ClientStatus = isConnected ? _resourceService.FindResource<string>("HomePage.LCUConnected") : _resourceService.FindResource<string>("HomePage.LCUDisconnected");
            });
            _eventAggregator.GetEvent<LanguageSwitchedEvent>().Subscribe(() =>
            {
                ClientStatus = IsConnected ? _resourceService.FindResource<string>("HomePage.LCUConnected") : _resourceService.FindResource<string>("HomePage.LCUDisconnected");
            });

        }
        private string _clientStatus;
        public string ClientStatus
        {
            get { return _clientStatus; }
            set { SetProperty(ref _clientStatus, value); }
        }

        private bool _isConnected;
        public bool IsConnected
        {
            get { return _isConnected; }
            set { SetProperty(ref _isConnected, value); }
        }
    }
}

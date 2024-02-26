using Prism.Events;
using Prism.Ioc;
using Prism.Regions;
using Prometheus.Core;
using Prometheus.Core.Events;
using Prometheus.Core.Models;
using Prometheus.Core.Mvvm;
using Prometheus.Services.Interfaces;
using Prometheus.Services.Interfaces.Client;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;

namespace Prometheus.Modules.Home.ViewModels
{
    public class HomeViewModel : RegionViewModelBase
    {
        private readonly IEventAggregator _eventAggregator;
        private readonly IGameResourceManager _gameResourceManager;
        private readonly IResourceService _resourceService;
        private readonly IContainerExtension _containerExtension;
        private readonly IHttpService _httpService;

        public HomeViewModel(IRegionManager regionManager, IEventAggregator eventAggregator, IGameResourceManager gameResourceManager,
            IResourceService resourceService, IContainerExtension containerExtension, IHttpService httpService) : base(regionManager)
        {
            _eventAggregator = eventAggregator;
            _gameResourceManager = gameResourceManager;
            _resourceService = resourceService;
            _containerExtension = containerExtension;
            _httpService = httpService;
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

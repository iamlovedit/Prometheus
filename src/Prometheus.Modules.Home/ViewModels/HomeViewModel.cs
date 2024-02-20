using Prism.Events;
using Prism.Regions;
using Prometheus.Core.Events;
using Prometheus.Core.Mvvm;
using System.Windows;

namespace Prometheus.Modules.Home.ViewModels
{
    public class HomeViewModel : RegionViewModelBase
    {
        private readonly IEventAggregator _eventAggregator;


        public HomeViewModel(IRegionManager regionManager, IEventAggregator eventAggregator) : base(regionManager)
        {
            _eventAggregator = eventAggregator;
            _eventAggregator.GetEvent<ConnectLCUEvent>().Subscribe(isConnected =>
            {
                if (isConnected) 
                {
                    ClientStatus = Application.Current.FindResource("HomePage.LCUConnected").ToString();
                    IsConnected = true;
                }
                else
                {
                    ClientStatus = Application.Current.FindResource("HomePage.LCUDisconnected").ToString();
                    IsConnected = false;
                }
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

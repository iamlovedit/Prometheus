using Prism.Events;
using Prism.Mvvm;
using Prism.Regions;

namespace Prometheus.Modules.Home.ViewModels
{
    public class HomeViewModel : BindableBase, INavigationAware
    {
        private readonly IEventAggregator _eventAggregator;
        public HomeViewModel(IEventAggregator eventAggregator)
        {
            _eventAggregator = eventAggregator;
        }

        private string _clientStatus;
        public string ClientStatus
        {
            get { return _clientStatus; }
            set { SetProperty(ref _clientStatus, value); }
        }

        public void OnNavigatedTo(NavigationContext navigationContext)
        {

        }

        public bool IsNavigationTarget(NavigationContext navigationContext)
        {
            return true;
        }

        public void OnNavigatedFrom(NavigationContext navigationContext)
        {

        }
    }
}

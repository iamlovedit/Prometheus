using Prism.Mvvm;
using Prism.Regions;
using Prometheus.Core;

namespace Prometheus.Modules.Home.ViewModels
{
    public class HomeViewModel : BindableBase, INavigationAware
    {
        public HomeViewModel()
        {

        }

        private string _clientStatus;
        public string ClientStatus
        {
            get { return _clientStatus; }
            set { SetProperty(ref _clientStatus, value); }
        }

        public void OnNavigatedTo(NavigationContext navigationContext)
        {
            if (navigationContext.Parameters.TryGetValue<string>(ParameterNames.Client_Status, out var status))
            {
                ClientStatus = status;
            }
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

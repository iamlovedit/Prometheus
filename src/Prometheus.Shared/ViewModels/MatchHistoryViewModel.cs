using Prism.Ioc;
using Prism.Regions;
using Prometheus.Core.Models;
using Prometheus.Core.Mvvm;
using System.Collections.Generic;

namespace Prometheus.Shared.ViewModels
{
    public class MatchHistoryViewModel : RegionViewModelBase
    {
        public MatchHistoryViewModel(IRegionManager regionManager, IContainerExtension containerExtension) : base(regionManager)
        {

        }


        private List<Match> _matches;
        public List<Match> Matches
        {
            get { return _matches; }
            set { SetProperty(ref _matches, value); }
        }

        public override void OnNavigatedTo(NavigationContext navigationContext)
        {

        }
    }
}

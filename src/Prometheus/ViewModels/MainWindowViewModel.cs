using Prism.Commands;
using Prism.Mvvm;
using Prism.Regions;
using Prometheus.Core;

namespace Prometheus.ViewModels
{
    public class MainWindowViewModel : BindableBase
    {

        private readonly IRegionManager _regionManager;

        private string _title = "Prometheus";
        public string Title
        {
            get { return _title; }
            set { SetProperty(ref _title, value); }
        }

        public MainWindowViewModel(IRegionManager regionManager)
        {
            _regionManager = regionManager;
        }

        private DelegateCommand _homeCommand;
        public DelegateCommand HomeCommand =>
            _homeCommand ?? (_homeCommand = new DelegateCommand(ExecuteHomeCommand));
        void ExecuteHomeCommand()
        {
            _regionManager.RequestNavigate(RegionNames.ContentRegion, RegionNames.HomeView);
        }

        private DelegateCommand _careerCommand;
        public DelegateCommand CareerCommand =>
            _careerCommand ?? (_careerCommand = new DelegateCommand(ExecuteCareerCommand));
        void ExecuteCareerCommand()
        {
            _regionManager.RequestNavigate(RegionNames.ContentRegion, RegionNames.CareerView);
        }

        private DelegateCommand _inventoryComamnd;
        public DelegateCommand InventoryCommand =>
            _inventoryComamnd ?? (_inventoryComamnd = new DelegateCommand(ExecuteInventoryCommand));
        void ExecuteInventoryCommand()
        {
            _regionManager.RequestNavigate(RegionNames.ContentRegion, RegionNames.InventoryView);
        }

        private DelegateCommand _searchCommand;
        public DelegateCommand SearchCommand =>
            _searchCommand ?? (_searchCommand = new DelegateCommand(ExecuteSearchCommand));
        void ExecuteSearchCommand()
        {
            _regionManager.RequestNavigate(RegionNames.ContentRegion, RegionNames.SearchView);
        }

        private DelegateCommand _utilityCommand;
        public DelegateCommand UtilityCommand =>
            _utilityCommand ?? (_utilityCommand = new DelegateCommand(ExecuteUtilityCommand));
        void ExecuteUtilityCommand()
        {
            _regionManager.RequestNavigate(RegionNames.ContentRegion, RegionNames.UtilityView);
        }

        private DelegateCommand _matchCommand;
        public DelegateCommand MatchCommand =>
            _matchCommand ?? (_matchCommand = new DelegateCommand(ExecuteMatchCommand));
        void ExecuteMatchCommand()
        {
            _regionManager.RequestNavigate(RegionNames.ContentRegion, RegionNames.MatchView);
        }


        private DelegateCommand _settingCommand;
        public DelegateCommand SettingCommand =>
            _settingCommand ?? (_settingCommand = new DelegateCommand(ExecuteSettingCommand));
        void ExecuteSettingCommand()
        {
            _regionManager.RequestNavigate(RegionNames.ContentRegion, RegionNames.SettingView);
        }
    }
}

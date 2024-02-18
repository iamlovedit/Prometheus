using Prism.Commands;
using Prism.Events;
using Prism.Mvvm;
using Prism.Regions;
using Prometheus.Core;
using Prometheus.Core.Events;
using Prometheus.Services.Interfaces;
using System.Windows;

namespace Prometheus.ViewModels
{
    public class MainWindowViewModel : BindableBase
    {
        private string _token;
        private string _port;
        private readonly IRegionManager _regionManager;
        private readonly IProcessService _processService;
        private readonly IEventAggregator _eventAggregator;

        public MainWindowViewModel(IRegionManager regionManager, IProcessService processService, IEventAggregator eventAggregator)
        {
            _regionManager = regionManager;
            _processService = processService;
            _eventAggregator = eventAggregator;
        }

        private string _title = "Prometheus";
        public string Title
        {
            get { return _title; }
            set { SetProperty(ref _title, value); }
        }


        private DelegateCommand _loadedCommand;
        public DelegateCommand LoadedCommand =>
            _loadedCommand ?? (_loadedCommand = new DelegateCommand(ExecuteLoadedCommand));
        void ExecuteLoadedCommand()
        {
            _eventAggregator.GetEvent<TitleChangeEvent>().Subscribe(v =>
            {
                Title = "Prometheus--" + v;
            });
            var argsMap = _processService.GetProcessCommandLines();
            _port = argsMap["--app-port"];
            _token = argsMap["--remoting-auth-token"];
            _regionManager.RequestNavigate(RegionNames.ContentRegion, RegionNames.HomeView, new NavigationParameters()
            {
                {ParameterNames.Client_Status, Application.Current.FindResource("HomePage.ClientConnected").ToString()}
            });
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

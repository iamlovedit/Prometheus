using Prism.Commands;
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

        private bool _autoReconnect;
        public bool AutoReconnect
        {
            get { return _autoReconnect; }
            set { SetProperty(ref _autoReconnect, value); }
        }

        private bool _autoAccept;
        public bool AutoAccept
        {
            get { return _autoAccept; }
            set { SetProperty(ref _autoAccept, value); }
        }

        private int _selectedStatusIndex;
        public int SelectedStatusIndex
        {
            get { return _selectedStatusIndex; }
            set { SetProperty(ref _selectedStatusIndex, value); }
        }


        private int _selectedModeIndex;
        public int SelectedModeIndex
        {
            get { return _selectedModeIndex; }
            set { SetProperty(ref _selectedModeIndex, value); }
        }

        private int _selectedTierIndex;
        public int SelectedTierIndex
        {
            get { return _selectedTierIndex; }
            set { SetProperty(ref _selectedTierIndex, value); }
        }

        private int _selectedDivisionIndex;
        public int SelectedDivisionIndex
        {
            get { return _selectedDivisionIndex; }
            set { SetProperty(ref _selectedDivisionIndex, value); }
        }

        public string LobbyName { get; set; }

        public string LobbyPassword { get; set; }


        private DelegateCommand _createLobbyCommand;
        public DelegateCommand CreateLobbyCommand =>
            _createLobbyCommand ?? (_createLobbyCommand = new DelegateCommand(ExecuteCreateLobbyCommand));
        void ExecuteCreateLobbyCommand()
        {

        }

        private DelegateCommand _configChampionCommand;
        public DelegateCommand ConfigChampionCommand =>
            _configChampionCommand ?? (_configChampionCommand = new DelegateCommand(ExecuteConfigChampionCommand));
        void ExecuteConfigChampionCommand()
        {

        }


        private DelegateCommand _tierComfirmCommand;
        public DelegateCommand TierComfirmCommand =>
            _tierComfirmCommand ?? (_tierComfirmCommand = new DelegateCommand(ExecuteComfirmCommand));
        void ExecuteComfirmCommand()
        {

        }
    }
}

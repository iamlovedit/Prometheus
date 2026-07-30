using HandyControl.Controls;
using Prism.Commands;
using Prism.Regions;
using Prometheus.Core.Models;
using Prometheus.Core.Mvvm;
using Prometheus.Core.Tasks;
using Prometheus.Services.Interfaces.Client;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Prometheus.Modules.Utility.ViewModels
{
    public class UtilityViewModel : RegionViewModelBase
    {
        private readonly static Dictionary<int, string> _statusMap = new()
        {
            {0,"chat"},
            {1,"away"},
            {2,"offline"},
        };
        private readonly static Dictionary<int, Tier> _tierMap = new()
        {
            {0,Tier.UNRANKED },
            {1,Tier.IRON },
            {2,Tier.BRONZE },
            {3,Tier.SILVER },
            {4,Tier.GOLD },
            {5,Tier.PLATINUM },
            {6,Tier.EMERALD },
            {7,Tier.DIAMOND },
            {8,Tier.MASTER },
            {9,Tier.GRANDMASTER },
            {10,Tier.CHALLENGER },
        };
        private readonly static Dictionary<int, QueueType> _ququeMap = new()
        {
            {0,QueueType.RANKED_TFT },
            {1,QueueType.RANKED_SOLO_5x5 },
            {2,QueueType.RANKED_FLEX_SR},
        };
        private readonly static Dictionary<int, Division> _divsionMap = new()
        {
            {-1,Division.NA },
            {0,Division.I },
            {1,Division.II },
            {2,Division.III},
            {3,Division.IV},
        };


        private readonly IClientService _clientService;
        private readonly IResourceService _resourceService;
        private readonly IGameService _gameService;
        private readonly IGameAutomationSettings _automationSettings;

        public UtilityViewModel(IRegionManager regionManager, IClientService clientService,
            IResourceService resourceService,
            IGameService gameService,
            IGameAutomationSettings automationSettings) : base(regionManager)
        {
            _clientService = clientService;
            _resourceService = resourceService;
            _gameService = gameService;
            _automationSettings = automationSettings;
            _automationSettings.Changed += HandleAutomationChanged;
            InitializeAsync().Observe("Loading League client installation data");
        }

        private string _installtionPath;
        public string InstalltionPath
        {
            get { return _installtionPath; }
            set { SetProperty(ref _installtionPath, value); }
        }

        private async Task InitializeAsync()
        {
            InstalltionPath = await _clientService.GetInstallLocation();
        }

        public bool AutoReconnect
        {
            get { return _automationSettings.AutoReconnect; }
            set
            {
                if (_automationSettings.AutoReconnect == value)
                {
                    return;
                }
                _automationSettings.AutoReconnect = value;
                RaisePropertyChanged();
            }
        }

        public bool AutoAccept
        {
            get { return _automationSettings.AutoAcceptReadyCheck; }
            set
            {
                if (_automationSettings.AutoAcceptReadyCheck == value)
                {
                    return;
                }
                _automationSettings.AutoAcceptReadyCheck = value;
                RaisePropertyChanged();
            }
        }

        private void HandleAutomationChanged(object sender, EventArgs e)
        {
            RaisePropertyChanged(nameof(AutoAccept));
            RaisePropertyChanged(nameof(AutoReconnect));
        }

        public override void Destroy()
        {
            _automationSettings.Changed -= HandleAutomationChanged;
            base.Destroy();
        }

        private int _selectedStatusIndex = -1;
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
            set
            {
                SetProperty(ref _selectedTierIndex, value);
                if (value > 7)
                {
                    _selectedDivisionIndex = -1;
                }
            }
        }

        private int _selectedDivisionIndex;
        public int SelectedDivisionIndex
        {
            get { return _selectedDivisionIndex; }
            set { SetProperty(ref _selectedDivisionIndex, value); }
        }

        public string LobbyName { get; set; }

        public string LobbyPassword { get; set; } = "";

        public string Status { get; set; }

        private DelegateCommand _statusComfirmStatus;
        public DelegateCommand StatusComfirmStatus =>
            _statusComfirmStatus ?? (_statusComfirmStatus = new DelegateCommand(ExecuteStatusComfirmStatus));
        void ExecuteStatusComfirmStatus()
        {
            ExecuteStatusComfirmStatusAsync().Observe("Updating League chat status message");
        }

        private async Task ExecuteStatusComfirmStatusAsync()
        {
            try
            {
                await _gameService.SetStatusAsync(Status);
            }
            catch (Exception e)
            {
                Growl.Error(e.Message);
            }

        }

        private DelegateCommand _chatStausChangedCommand;
        public DelegateCommand ChatStatusChangedCommand =>
            _chatStausChangedCommand ?? (_chatStausChangedCommand = new DelegateCommand(ExecuteChatStatusChangedCommand));
        void ExecuteChatStatusChangedCommand()
        {
            ExecuteChatStatusChangedCommandAsync().Observe("Updating League online status");
        }

        private async Task ExecuteChatStatusChangedCommandAsync()
        {
            await _gameService.SetOnlineStatusAsync(_statusMap[_selectedStatusIndex]);
        }

        private DelegateCommand _createLobbyCommand;
        public DelegateCommand CreateLobbyCommand =>
            _createLobbyCommand ?? (_createLobbyCommand = new DelegateCommand(ExecuteCreateLobbyCommand));
        void ExecuteCreateLobbyCommand()
        {
            ExecuteCreateLobbyCommandAsync().Observe("Creating a practice lobby");
        }

        private async Task ExecuteCreateLobbyCommandAsync()
        {
            if (string.IsNullOrEmpty(LobbyName))
            {
                Growl.Error(_resourceService.FindResource<string>("Errors.EmptyLobbyName"));
                return;
            }
            try
            {
                await _gameService.CreatePracticeLobbyAsync(LobbyName, LobbyPassword);
                Growl.Info(_resourceService.FindResource<string>("Infos.CreateLobbySuccesfully"));
            }
            catch (Exception e)
            {
                Growl.Error(e.Message);
            }
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
            ExecuteComfirmCommandAsync().Observe("Updating displayed League rank");
        }

        private async Task ExecuteComfirmCommandAsync()
        {
            await _gameService.SetChatTierAsync(_ququeMap[_selectedModeIndex], _tierMap[_selectedTierIndex], _divsionMap[_selectedDivisionIndex]);
        }
    }
}

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
        private static readonly Dictionary<int, string> _statusMap = new()
        {
            { 0, "chat" },
            { 1, "away" },
            { 2, "offline" }
        };

        private static readonly Dictionary<int, Tier> _tierMap = new()
        {
            { 0, Tier.UNRANKED },
            { 1, Tier.IRON },
            { 2, Tier.BRONZE },
            { 3, Tier.SILVER },
            { 4, Tier.GOLD },
            { 5, Tier.PLATINUM },
            { 6, Tier.EMERALD },
            { 7, Tier.DIAMOND },
            { 8, Tier.MASTER },
            { 9, Tier.GRANDMASTER },
            { 10, Tier.CHALLENGER }
        };

        private static readonly Dictionary<int, QueueType> _queueMap = new()
        {
            { 0, QueueType.RANKED_TFT },
            { 1, QueueType.RANKED_SOLO_5x5 },
            { 2, QueueType.RANKED_FLEX_SR }
        };

        private static readonly Dictionary<int, Division> _divisionMap = new()
        {
            { -1, Division.NA },
            { 0, Division.I },
            { 1, Division.II },
            { 2, Division.III },
            { 3, Division.IV }
        };

        private readonly IResourceService _resourceService;
        private readonly IGameService _gameService;

        public UtilityViewModel(
            IRegionManager regionManager,
            IResourceService resourceService,
            IGameService gameService)
            : base(regionManager)
        {
            _resourceService = resourceService;
            _gameService = gameService;
        }

        private int _selectedStatusIndex = -1;
        public int SelectedStatusIndex
        {
            get => _selectedStatusIndex;
            set => SetProperty(ref _selectedStatusIndex, value);
        }

        private int _selectedModeIndex;
        public int SelectedModeIndex
        {
            get => _selectedModeIndex;
            set => SetProperty(ref _selectedModeIndex, value);
        }

        private int _selectedTierIndex;
        public int SelectedTierIndex
        {
            get => _selectedTierIndex;
            set
            {
                if (!SetProperty(ref _selectedTierIndex, value))
                {
                    return;
                }

                if (value == 0 || value > 7)
                {
                    SelectedDivisionIndex = -1;
                }
                else if (SelectedDivisionIndex < 0)
                {
                    SelectedDivisionIndex = 0;
                }
            }
        }

        private int _selectedDivisionIndex = -1;
        public int SelectedDivisionIndex
        {
            get => _selectedDivisionIndex;
            set => SetProperty(ref _selectedDivisionIndex, value);
        }

        private string _lobbyName;
        public string LobbyName
        {
            get => _lobbyName;
            set => SetProperty(ref _lobbyName, value);
        }

        private string _lobbyPassword = string.Empty;
        public string LobbyPassword
        {
            get => _lobbyPassword;
            set => SetProperty(ref _lobbyPassword, value);
        }

        private string _status;
        public string Status
        {
            get => _status;
            set => SetProperty(ref _status, value);
        }

        private DelegateCommand _confirmStatusCommand;
        public DelegateCommand ConfirmStatusCommand =>
            _confirmStatusCommand ??= new DelegateCommand(() =>
                UpdateStatusMessageAsync().Observe("Updating League chat status message"));

        private DelegateCommand _chatStatusChangedCommand;
        public DelegateCommand ChatStatusChangedCommand =>
            _chatStatusChangedCommand ??= new DelegateCommand(() =>
                UpdateOnlineStatusAsync().Observe("Updating League online status"));

        private DelegateCommand _createLobbyCommand;
        public DelegateCommand CreateLobbyCommand =>
            _createLobbyCommand ??= new DelegateCommand(() =>
                CreateLobbyAsync().Observe("Creating a practice lobby"));

        private DelegateCommand _applyTierCommand;
        public DelegateCommand ApplyTierCommand =>
            _applyTierCommand ??= new DelegateCommand(() =>
                ApplyTierAsync().Observe("Updating displayed League rank"));

        private async Task UpdateStatusMessageAsync()
        {
            try
            {
                await _gameService.SetStatusAsync(Status ?? string.Empty);
            }
            catch (Exception exception)
            {
                Growl.Error(exception.Message);
            }
        }

        private async Task UpdateOnlineStatusAsync()
        {
            if (!_statusMap.TryGetValue(SelectedStatusIndex, out var status))
            {
                return;
            }

            try
            {
                await _gameService.SetOnlineStatusAsync(status);
            }
            catch (Exception exception)
            {
                Growl.Error(exception.Message);
            }
        }

        private async Task CreateLobbyAsync()
        {
            if (string.IsNullOrWhiteSpace(LobbyName))
            {
                Growl.Error(_resourceService.FindResource<string>("Errors.EmptyLobbyName"));
                return;
            }

            try
            {
                await _gameService.CreatePracticeLobbyAsync(
                    LobbyName.Trim(),
                    LobbyPassword ?? string.Empty);
                Growl.Info(_resourceService.FindResource<string>("Infos.CreateLobbySuccesfully"));
            }
            catch (Exception exception)
            {
                Growl.Error(exception.Message);
            }
        }

        private async Task ApplyTierAsync()
        {
            if (!_queueMap.TryGetValue(SelectedModeIndex, out var queue)
                || !_tierMap.TryGetValue(SelectedTierIndex, out var tier)
                || !_divisionMap.TryGetValue(SelectedDivisionIndex, out var division))
            {
                Growl.Error(_resourceService.FindResource<string>("Utility.InvalidSelection"));
                return;
            }

            try
            {
                await _gameService.SetChatTierAsync(queue, tier, division);
            }
            catch (Exception exception)
            {
                Growl.Error(exception.Message);
            }
        }
    }
}

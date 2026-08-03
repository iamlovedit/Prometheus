using HandyControl.Controls;
using Prism.Commands;
using Prism.Regions;
using Prometheus.Core.Logging;
using Prometheus.Core.Models;
using Prometheus.Core.Mvvm;
using Prometheus.Core.Tasks;
using Prometheus.Services.Interfaces.Client;
using Serilog;
using Serilog.Events;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Data;

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
        private readonly IProfilePresentationSettings _profileSettings;
        private readonly IGameResourceManager _gameResourceManager;
        private readonly IGameAutomationSettings _automationSettings;
        private readonly ILcuCompanionSettings _companionSettings;
        private bool _isCompanionSettingsSubscribed;

        public UtilityViewModel(
            IRegionManager regionManager,
            IResourceService resourceService,
            IGameService gameService,
            IProfilePresentationSettings profileSettings,
            IGameResourceManager gameResourceManager,
            IGameAutomationSettings automationSettings,
            ILcuCompanionSettings companionSettings)
            : base(regionManager)
        {
            _resourceService = resourceService;
            _gameService = gameService;
            _profileSettings = profileSettings;
            _gameResourceManager = gameResourceManager;
            _automationSettings = automationSettings;
            _companionSettings = companionSettings;
            PickChampionEditor = new ChampionPriorityEditorViewModel(
                EnsureChampionCatalogLoaded,
                PersistPreferredPickChampionIds);
            BanChampionEditor = new ChampionPriorityEditorViewModel(
                EnsureChampionCatalogLoaded,
                PersistPreferredBanChampionIds);

            _selectedStatusIndex = FindIndex(
                _statusMap, _profileSettings.OnlineStatus, -1);
            Status = _profileSettings.StatusMessage ?? string.Empty;

            if (_profileSettings.QueueType.HasValue &&
                _profileSettings.Tier.HasValue &&
                _profileSettings.Division.HasValue)
            {
                _selectedModeIndex = FindIndex(
                    _queueMap, _profileSettings.QueueType.Value, 0);
                _selectedTierIndex = FindIndex(
                    _tierMap, _profileSettings.Tier.Value, 0);
                _selectedDivisionIndex = FindIndex(
                    _divisionMap, _profileSettings.Division.Value, -1);
            }

            ApplyPreferredAramChampionIds(
                _automationSettings.PreferredAramChampionIds,
                []);
            PickChampionEditor.ApplyPreferredChampionIds(
                _automationSettings.PreferredPickChampionIds,
                []);
            BanChampionEditor.ApplyPreferredChampionIds(
                _automationSettings.PreferredBanChampionIds,
                []);
        }

        public override void OnNavigatedTo(NavigationContext navigationContext)
        {
            if (!_isCompanionSettingsSubscribed)
            {
                _companionSettings.PropertyChanged +=
                    HandleCompanionSettingsPropertyChanged;
                _isCompanionSettingsSubscribed = true;
            }

            RaisePropertyChanged(nameof(AutoSwapAramBench));
            RaisePropertyChanged(nameof(AutoPickChampion));
            RaisePropertyChanged(nameof(AutoBanChampion));
            RaisePropertyChanged(nameof(IsChampionSelectCompanionEnabled));
            LoadAramChampionsAsync().Observe("Loading champion automation preferences");
        }

        public override void OnNavigatedFrom(NavigationContext navigationContext)
        {
            if (_isCompanionSettingsSubscribed)
            {
                _companionSettings.PropertyChanged -=
                    HandleCompanionSettingsPropertyChanged;
                _isCompanionSettingsSubscribed = false;
            }
        }

        public ChampionPriorityEditorViewModel PickChampionEditor { get; }

        public ChampionPriorityEditorViewModel BanChampionEditor { get; }

        private int _selectedStatusIndex = -1;
        public int SelectedStatusIndex
        {
            get => _selectedStatusIndex;
            set
            {
                if (SetProperty(ref _selectedStatusIndex, value))
                {
                    UpdateOnlineStatusAsync().Observe("Updating League online status");
                }
            }
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

        private IReadOnlyList<ChampionSummary> _aramChampions = [];
        public IReadOnlyList<ChampionSummary> AramChampions
        {
            get => _aramChampions;
            private set => SetProperty(ref _aramChampions, value);
        }

        private ICollectionView _aramChampionOptions;
        public ICollectionView AramChampionOptions
        {
            get => _aramChampionOptions;
            private set => SetProperty(ref _aramChampionOptions, value);
        }

        private bool _isSynchronizingAramSelector;
        private string _aramChampionSearchText = string.Empty;
        public string AramChampionSearchText
        {
            get => _aramChampionSearchText;
            set
            {
                if (_isSynchronizingAramSelector)
                {
                    return;
                }

                var searchText = value ?? string.Empty;
                if (string.Equals(
                        _aramChampionSearchText,
                        searchText,
                        StringComparison.Ordinal))
                {
                    return;
                }

                _isSynchronizingAramSelector = true;
                try
                {
                    SetProperty(
                        ref _aramChampionSearchText,
                        searchText,
                        nameof(AramChampionSearchText));
                    var isSelectedChampionText = string.Equals(
                        searchText,
                        SelectedAramChampion?.Name,
                        StringComparison.CurrentCultureIgnoreCase);
                    if (!isSelectedChampionText && SelectedAramChampion is not null)
                    {
                        SetProperty(
                            ref _selectedAramChampion,
                            null,
                            nameof(SelectedAramChampion));
                        _addAramChampionCommand?.RaiseCanExecuteChanged();

                        // Clearing SelectedItem can make WPF clear the editable text.
                        // Re-publish the user's input while synchronization is guarded.
                        RaisePropertyChanged(nameof(AramChampionSearchText));
                    }

                    if (!isSelectedChampionText)
                    {
                        AramChampionOptions?.Refresh();
                        if (!string.IsNullOrWhiteSpace(searchText))
                        {
                            IsAramChampionDropDownOpen = true;
                        }
                    }
                }
                finally
                {
                    _isSynchronizingAramSelector = false;
                }
            }
        }

        private bool _isAramChampionDropDownOpen;
        public bool IsAramChampionDropDownOpen
        {
            get => _isAramChampionDropDownOpen;
            set => SetProperty(ref _isAramChampionDropDownOpen, value);
        }

        public ObservableCollection<ChampionSummary> PreferredAramChampions { get; } = [];

        private ChampionSummary _selectedAramChampion;
        public ChampionSummary SelectedAramChampion
        {
            get => _selectedAramChampion;
            set
            {
                if (SetProperty(ref _selectedAramChampion, value))
                {
                    if (!_isSynchronizingAramSelector && value is not null)
                    {
                        _isSynchronizingAramSelector = true;
                        try
                        {
                            SetProperty(
                                ref _aramChampionSearchText,
                                value.Name ?? string.Empty,
                                nameof(AramChampionSearchText));
                            IsAramChampionDropDownOpen = false;
                        }
                        finally
                        {
                            _isSynchronizingAramSelector = false;
                        }
                    }

                    _addAramChampionCommand?.RaiseCanExecuteChanged();
                }
            }
        }

        private ChampionSummary _selectedPreferredAramChampion;
        public ChampionSummary SelectedPreferredAramChampion
        {
            get => _selectedPreferredAramChampion;
            set
            {
                if (SetProperty(ref _selectedPreferredAramChampion, value))
                {
                    RaiseAramPreferenceCommandState();
                }
            }
        }

        private bool _isAramChampionListLoading;
        public bool IsAramChampionListLoading
        {
            get => _isAramChampionListLoading;
            private set => SetProperty(ref _isAramChampionListLoading, value);
        }

        public bool AutoSwapAramBench
        {
            get => _automationSettings.AutoSwapAramBench;
            set
            {
                var oldValue = _automationSettings.AutoSwapAramBench;
                if (oldValue == value)
                {
                    return;
                }

                _automationSettings.AutoSwapAramBench = value;
                RaisePropertyChanged();
                var persisted = _automationSettings.LastPersistenceSucceeded;
                OperationLog.Write(
                    persisted ? LogEventLevel.Information : LogEventLevel.Error,
                    "automation.aram_bench_swap.changed",
                    "Automation",
                    "Manual",
                    persisted ? "Succeeded" : "Failed",
                    Guid.NewGuid(),
                    "Utility",
                    persisted
                        ? value
                            ? "Automatic ARAM champion swapping was enabled."
                            : "Automatic ARAM champion swapping was disabled."
                        : "The automatic ARAM champion swap setting could not be saved.",
                    new Dictionary<string, object>
                    {
                        ["OldValue"] = oldValue,
                        ["NewValue"] = value
                    });
                if (!persisted)
                {
                    Growl.Warning(_resourceService.FindResource<string>(
                        "Utility.AramSwap.PersistenceFailed"));
                }
            }
        }

        public bool AutoPickChampion
        {
            get => _automationSettings.AutoPickChampion;
            set => SetChampionAutomationEnabled(
                _automationSettings.AutoPickChampion,
                value,
                newValue => _automationSettings.AutoPickChampion = newValue,
                nameof(AutoPickChampion),
                "automation.auto_pick.changed",
                "Automatic champion picking");
        }

        public bool AutoBanChampion
        {
            get => _automationSettings.AutoBanChampion;
            set => SetChampionAutomationEnabled(
                _automationSettings.AutoBanChampion,
                value,
                newValue => _automationSettings.AutoBanChampion = newValue,
                nameof(AutoBanChampion),
                "automation.auto_ban.changed",
                "Automatic champion banning");
        }

        public bool IsChampionSelectCompanionEnabled
        {
            get => _companionSettings.IsEnabled;
            set
            {
                if (_companionSettings.IsEnabled == value)
                {
                    return;
                }

                _companionSettings.IsEnabled = value;
                RaisePropertyChanged();
                if (!_companionSettings.LastPersistenceSucceeded)
                {
                    Growl.Warning(_resourceService.FindResource<string>(
                        "Utility.Companion.PersistenceFailed"));
                }
            }
        }

        private void HandleCompanionSettingsPropertyChanged(
            object sender,
            PropertyChangedEventArgs args)
        {
            if (string.IsNullOrEmpty(args?.PropertyName) ||
                args.PropertyName == nameof(ILcuCompanionSettings.IsEnabled))
            {
                RaisePropertyChanged(nameof(IsChampionSelectCompanionEnabled));
            }
        }

        private DelegateCommand _confirmStatusCommand;
        public DelegateCommand ConfirmStatusCommand =>
            _confirmStatusCommand ??= new DelegateCommand(() =>
                UpdateStatusMessageAsync().Observe("Updating League chat status message"));

        private DelegateCommand _createLobbyCommand;
        public DelegateCommand CreateLobbyCommand =>
            _createLobbyCommand ??= new DelegateCommand(() =>
                CreateLobbyAsync().Observe("Creating a practice lobby"));

        private DelegateCommand _applyTierCommand;
        public DelegateCommand ApplyTierCommand =>
            _applyTierCommand ??= new DelegateCommand(() =>
                ApplyTierAsync().Observe("Updating displayed League rank"));

        private DelegateCommand _addAramChampionCommand;
        public DelegateCommand AddAramChampionCommand =>
            _addAramChampionCommand ??= new DelegateCommand(
                AddAramChampion,
                CanAddAramChampion);

        private DelegateCommand _openAramChampionSelectorCommand;
        public DelegateCommand OpenAramChampionSelectorCommand =>
            _openAramChampionSelectorCommand ??= new DelegateCommand(() =>
            {
                EnsureChampionCatalogLoaded();
            });

        private DelegateCommand _removeAramChampionCommand;
        public DelegateCommand RemoveAramChampionCommand =>
            _removeAramChampionCommand ??= new DelegateCommand(
                RemoveAramChampion,
                () => SelectedPreferredAramChampion is not null);

        private DelegateCommand _moveAramChampionUpCommand;
        public DelegateCommand MoveAramChampionUpCommand =>
            _moveAramChampionUpCommand ??= new DelegateCommand(
                () => MoveAramChampion(-1),
                () => GetSelectedPreferredAramChampionIndex() > 0);

        private DelegateCommand _moveAramChampionDownCommand;
        public DelegateCommand MoveAramChampionDownCommand =>
            _moveAramChampionDownCommand ??= new DelegateCommand(
                () => MoveAramChampion(1),
                () =>
                {
                    var index = GetSelectedPreferredAramChampionIndex();
                    return index >= 0 && index < PreferredAramChampions.Count - 1;
                });

        private async Task LoadAramChampionsAsync()
        {
            if (IsAramChampionListLoading)
            {
                return;
            }

            if (AramChampions.Count > 0)
            {
                ApplyPreferredAramChampionIds(
                    _automationSettings.PreferredAramChampionIds,
                    AramChampions);
                PickChampionEditor.ApplyPreferredChampionIds(
                    _automationSettings.PreferredPickChampionIds,
                    AramChampions);
                BanChampionEditor.ApplyPreferredChampionIds(
                    _automationSettings.PreferredBanChampionIds,
                    AramChampions);
                return;
            }

            IsAramChampionListLoading = true;
            try
            {
                var champions = await _gameResourceManager.GetChampionSummarysAsync();
                if (champions is null || champions.Count == 0)
                {
                    Log.Debug(
                        "Champion selector data is unavailable; it will retry when opened");
                    return;
                }

                AramChampions = champions
                    .Where(champion => champion is not null && champion.Id > 0)
                    .OrderBy(champion => champion.Name, StringComparer.CurrentCultureIgnoreCase)
                    .ToArray();
                CreateAramChampionOptions();
                ApplyPreferredAramChampionIds(
                    _automationSettings.PreferredAramChampionIds,
                    AramChampions);
                PickChampionEditor.ApplyPreferredChampionIds(
                    _automationSettings.PreferredPickChampionIds,
                    AramChampions);
                PickChampionEditor.SetChampionCatalog(AramChampions);
                BanChampionEditor.ApplyPreferredChampionIds(
                    _automationSettings.PreferredBanChampionIds,
                    AramChampions);
                BanChampionEditor.SetChampionCatalog(AramChampions);

                await LoadAramChampionIconsAsync(AramChampions);
            }
            catch (Exception exception)
            {
                Growl.Error(exception.Message);
            }
            finally
            {
                IsAramChampionListLoading = false;
                _addAramChampionCommand?.RaiseCanExecuteChanged();
            }
        }

        private void ApplyPreferredAramChampionIds(
            IEnumerable<int> championIds,
            IReadOnlyList<ChampionSummary> champions)
        {
            var championMap = champions?
                .Where(champion => champion is not null && champion.Id > 0)
                .ToDictionary(champion => champion.Id) ?? [];
            PreferredAramChampions.Clear();
            foreach (var championId in championIds?
                         .Where(championId => championId > 0)
                         .Distinct() ?? [])
            {
                if (!championMap.TryGetValue(championId, out var champion))
                {
                    champion = new ChampionSummary
                    {
                        Id = championId,
                        Name = $"#{championId}",
                        Alias = string.Empty
                    };
                }

                PreferredAramChampions.Add(champion);
            }

            SelectedPreferredAramChampion = PreferredAramChampions.FirstOrDefault();
            RaiseAramPreferenceCommandState();
        }

        private void CreateAramChampionOptions()
        {
            var options = CollectionViewSource.GetDefaultView(AramChampions);
            options.Filter = FilterAramChampion;
            AramChampionOptions = options;
        }

        private bool FilterAramChampion(object item)
        {
            if (item is not ChampionSummary champion || champion.Id <= 0)
            {
                return false;
            }

            var keyword = AramChampionSearchText?.Trim();
            return string.IsNullOrWhiteSpace(keyword) ||
                   (champion.Name?.Contains(
                       keyword, StringComparison.CurrentCultureIgnoreCase) ?? false) ||
                   (champion.Alias?.Contains(
                       keyword, StringComparison.OrdinalIgnoreCase) ?? false);
        }

        private async Task LoadAramChampionIconsAsync(
            IReadOnlyList<ChampionSummary> champions)
        {
            using var concurrencyGate = new SemaphoreSlim(8, 8);
            var tasks = champions.Select(async champion =>
            {
                await concurrencyGate.WaitAsync().ConfigureAwait(false);
                try
                {
                    champion.IconUri = await _gameResourceManager
                        .GetChampoinIconByIdAsync(champion.Id)
                        .ConfigureAwait(false);
                }
                catch (Exception exception)
                {
                    Log.Warning(exception,
                        "Unable to load champion icon {ChampionId} for the ARAM selector",
                        champion.Id);
                }
                finally
                {
                    concurrencyGate.Release();
                }
            });

            await Task.WhenAll(tasks).ConfigureAwait(false);
        }

        private bool CanAddAramChampion()
        {
            return SelectedAramChampion is not null &&
                   PreferredAramChampions.All(
                       champion => champion.Id != SelectedAramChampion.Id);
        }

        private void AddAramChampion()
        {
            if (!CanAddAramChampion())
            {
                return;
            }

            PreferredAramChampions.Add(SelectedAramChampion);
            SelectedPreferredAramChampion = SelectedAramChampion;
            PersistPreferredAramChampions();
            SelectedAramChampion = null;
            AramChampionSearchText = string.Empty;
        }

        private void RemoveAramChampion()
        {
            var selected = SelectedPreferredAramChampion;
            var selectedIndex = GetSelectedPreferredAramChampionIndex();
            if (selected is null || selectedIndex < 0)
            {
                return;
            }

            PreferredAramChampions.RemoveAt(selectedIndex);
            SelectedPreferredAramChampion = PreferredAramChampions.Count == 0
                ? null
                : PreferredAramChampions[Math.Min(
                    selectedIndex, PreferredAramChampions.Count - 1)];
            PersistPreferredAramChampions();
        }

        private void MoveAramChampion(int offset)
        {
            var selectedIndex = GetSelectedPreferredAramChampionIndex();
            var targetIndex = selectedIndex + offset;
            if (selectedIndex < 0 || targetIndex < 0 ||
                targetIndex >= PreferredAramChampions.Count)
            {
                return;
            }

            PreferredAramChampions.Move(selectedIndex, targetIndex);
            PersistPreferredAramChampions();
            RaiseAramPreferenceCommandState();
        }

        private int GetSelectedPreferredAramChampionIndex()
        {
            return SelectedPreferredAramChampion is null
                ? -1
                : PreferredAramChampions.IndexOf(SelectedPreferredAramChampion);
        }

        private void PersistPreferredAramChampions()
        {
            var oldChampionIds = _automationSettings.PreferredAramChampionIds?.ToArray() ?? [];
            var championIds = PreferredAramChampions
                .Select(champion => champion.Id)
                .ToArray();
            if (oldChampionIds.SequenceEqual(championIds))
            {
                return;
            }

            _automationSettings.PreferredAramChampionIds = championIds;
            var persisted = _automationSettings.LastPersistenceSucceeded;
            OperationLog.Write(
                persisted ? LogEventLevel.Information : LogEventLevel.Error,
                "automation.aram_bench_preferences.changed",
                "Automation",
                "Manual",
                persisted ? "Succeeded" : "Failed",
                Guid.NewGuid(),
                "Utility",
                persisted
                    ? "The preferred ARAM champion list was updated."
                    : "The preferred ARAM champion list could not be saved.",
                new Dictionary<string, object>
                {
                    ["OldCount"] = oldChampionIds.Length,
                    ["NewCount"] = championIds.Length
                });
            if (!persisted)
            {
                Growl.Warning(_resourceService.FindResource<string>(
                    "Utility.AramSwap.PersistenceFailed"));
            }
            _addAramChampionCommand?.RaiseCanExecuteChanged();
        }

        private void EnsureChampionCatalogLoaded()
        {
            if (AramChampions.Count == 0 && !IsAramChampionListLoading)
            {
                LoadAramChampionsAsync().Observe(
                    "Retrying champion automation selector loading");
            }
        }

        private void PersistPreferredPickChampionIds(IReadOnlyList<int> championIds)
        {
            PersistChampionAutomationPreferences(
                _automationSettings.PreferredPickChampionIds,
                championIds,
                value => _automationSettings.PreferredPickChampionIds = value,
                "automation.auto_pick_preferences.changed",
                "The automatic pick priority list was updated.",
                "The automatic pick priority list could not be saved.");
        }

        private void PersistPreferredBanChampionIds(IReadOnlyList<int> championIds)
        {
            PersistChampionAutomationPreferences(
                _automationSettings.PreferredBanChampionIds,
                championIds,
                value => _automationSettings.PreferredBanChampionIds = value,
                "automation.auto_ban_preferences.changed",
                "The automatic ban priority list was updated.",
                "The automatic ban priority list could not be saved.");
        }

        private void PersistChampionAutomationPreferences(
            IReadOnlyList<int> oldChampionIds,
            IReadOnlyList<int> championIds,
            Action<IReadOnlyList<int>> persist,
            string eventName,
            string successMessage,
            string failureMessage)
        {
            var oldIds = oldChampionIds?.ToArray() ?? [];
            var newIds = championIds?
                .Where(championId => championId > 0)
                .Distinct()
                .ToArray() ?? [];
            if (oldIds.SequenceEqual(newIds))
            {
                return;
            }

            persist(newIds);
            var persisted = _automationSettings.LastPersistenceSucceeded;
            OperationLog.Write(
                persisted ? LogEventLevel.Information : LogEventLevel.Error,
                eventName,
                "Automation",
                "Manual",
                persisted ? "Succeeded" : "Failed",
                Guid.NewGuid(),
                "Utility",
                persisted ? successMessage : failureMessage,
                new Dictionary<string, object>
                {
                    ["OldCount"] = oldIds.Length,
                    ["NewCount"] = newIds.Length
                });
            if (!persisted)
            {
                Growl.Warning(_resourceService.FindResource<string>(
                    "Utility.ChampionAutomation.PersistenceFailed"));
            }
        }

        private void SetChampionAutomationEnabled(
            bool oldValue,
            bool newValue,
            Action<bool> persist,
            string propertyName,
            string eventName,
            string displayName)
        {
            if (oldValue == newValue)
            {
                return;
            }

            persist(newValue);
            RaisePropertyChanged(propertyName);
            var persisted = _automationSettings.LastPersistenceSucceeded;
            OperationLog.Write(
                persisted ? LogEventLevel.Information : LogEventLevel.Error,
                eventName,
                "Automation",
                "Manual",
                persisted ? "Succeeded" : "Failed",
                Guid.NewGuid(),
                "Utility",
                persisted
                    ? $"{displayName} was {(newValue ? "enabled" : "disabled")}."
                    : $"The {displayName.ToLowerInvariant()} setting could not be saved.",
                new Dictionary<string, object>
                {
                    ["OldValue"] = oldValue,
                    ["NewValue"] = newValue
                });
            if (!persisted)
            {
                Growl.Warning(_resourceService.FindResource<string>(
                    "Utility.ChampionAutomation.PersistenceFailed"));
            }
        }

        private void RaiseAramPreferenceCommandState()
        {
            _addAramChampionCommand?.RaiseCanExecuteChanged();
            _removeAramChampionCommand?.RaiseCanExecuteChanged();
            _moveAramChampionUpCommand?.RaiseCanExecuteChanged();
            _moveAramChampionDownCommand?.RaiseCanExecuteChanged();
        }

        private async Task UpdateStatusMessageAsync()
        {
            try
            {
                var statusMessage = Status ?? string.Empty;
                await _gameService.SetStatusAsync(statusMessage);
                _profileSettings.SaveStatusMessage(statusMessage);
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
                _profileSettings.SaveOnlineStatus(status);
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
                _profileSettings.SaveTier(queue, tier, division);
            }
            catch (Exception exception)
            {
                Growl.Error(exception.Message);
            }
        }

        private static int FindIndex<T>(
            IReadOnlyDictionary<int, T> map,
            T value,
            int defaultIndex)
        {
            foreach (var item in map)
            {
                if (EqualityComparer<T>.Default.Equals(item.Value, value))
                {
                    return item.Key;
                }
            }

            return defaultIndex;
        }
    }
}

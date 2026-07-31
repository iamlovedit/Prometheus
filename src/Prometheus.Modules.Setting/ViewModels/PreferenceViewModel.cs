#nullable enable

using Prism.Commands;
using Prism.Events;
using Prism.Services.Dialogs;
using Prometheus.Core;
using Prometheus.Core.Events;
using Prometheus.Core.Models;
using Prometheus.Modules.Setting.Properties;
using Prometheus.Services.Interfaces;
using Prometheus.Services.Interfaces.Client;
using Prometheus.Services.Interfaces.Updates;
using System.Reflection;
using System.Windows;
using System.Windows.Threading;

namespace Prometheus.Modules.Setting.ViewModels
{
    public class PreferenceViewModel : TabItemViewModelBase
    {
        private readonly IGameAutomationSettings _automationSettings;
        private readonly IMatchService _matchService;
        private readonly ILogHistoryService _logHistory;
        private readonly IUpdateService _updateService;
        private readonly IDialogService _dialogService;

        protected override string TitleResourceKey { get; set; } = "Setting.Personalization";

        public PreferenceViewModel(
            IEventAggregator eventAggregator,
            IResourceService resourceService,
            IGameAutomationSettings automationSettings,
            IMatchService matchService,
            ILogHistoryService logHistory,
            IUpdateService updateService,
            IDialogService dialogService)
            : base(eventAggregator, resourceService)
        {
            _automationSettings = automationSettings;
            _matchService = matchService;
            _logHistory = logHistory;
            _updateService = updateService;
            _dialogService = dialogService;

            _selectedLanguageIndex = Settings.Default.LanguageIndex;
            _selectedThemeIndex = Settings.Default.ThemeIndex;
            _connectionState = matchService.Current?.ConnectionState ?? ConnectionState.Disconnected;
            _logCount = logHistory.GetSnapshot().Count;

            ApplicationVersion = GetApplicationVersion();
            LogCapacity = logHistory.Capacity;
            RefreshConnectionStatus();
            RefreshUpdateState();
            CheckForUpdatesCommand = new DelegateCommand(CheckForUpdates,
                () => !IsUpdateBusy);

            automationSettings.Changed += HandleAutomationChanged;
            matchService.SnapshotChanged += HandleSnapshotChanged;
            logHistory.EntryLogged += HandleLogChanged;
            logHistory.Cleared += HandleLogChanged;
            updateService.StateChanged += HandleUpdateStateChanged;
            EventAggregator.GetEvent<LanguageSwitchedEvent>().Subscribe(RefreshConnectionStatus);
            EventAggregator.GetEvent<LanguageSwitchedEvent>().Subscribe(RefreshUpdateState);
        }

        private int _selectedLanguageIndex;
        public int SelectedLanguageIndex
        {
            get => _selectedLanguageIndex;
            set
            {
                if (value is < 0 or > 1 || !SetProperty(ref _selectedLanguageIndex, value))
                {
                    return;
                }

                ResourceService.SwitchLanguage(value);
                Settings.Default.LanguageIndex = value;
                Settings.Default.Save();
                EventAggregator.GetEvent<LanguageSwitchedEvent>().Publish();
            }
        }

        private int _selectedThemeIndex;
        public int SelectedThemeIndex
        {
            get => _selectedThemeIndex;
            set
            {
                if (!Enum.IsDefined((ApplicationThemeMode)value)
                    || !SetProperty(ref _selectedThemeIndex, value))
                {
                    return;
                }

                ResourceService.SwitchTheme(value);
                Settings.Default.ThemeIndex = value;
                Settings.Default.Save();
            }
        }

        public bool AutoAccept
        {
            get => _automationSettings.AutoAcceptReadyCheck;
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

        public bool AutoReconnect
        {
            get => _automationSettings.AutoReconnect;
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

        private ConnectionState _connectionState;
        public ConnectionState ConnectionState
        {
            get => _connectionState;
            private set
            {
                if (SetProperty(ref _connectionState, value))
                {
                    RefreshConnectionStatus();
                }
            }
        }

        private string _connectionStatus;
        public string ConnectionStatus
        {
            get => _connectionStatus;
            private set => SetProperty(ref _connectionStatus, value);
        }

        private int _logCount;
        public int LogCount
        {
            get => _logCount;
            private set => SetProperty(ref _logCount, value);
        }

        public int LogCapacity { get; }

        public string ApplicationVersion { get; }

        public DelegateCommand CheckForUpdatesCommand { get; }

        private string _updateStatus = string.Empty;
        public string UpdateStatus
        {
            get => _updateStatus;
            private set => SetProperty(ref _updateStatus, value);
        }

        private double _updateProgress;
        public double UpdateProgress
        {
            get => _updateProgress;
            private set => SetProperty(ref _updateProgress, value);
        }

        private bool _isUpdateProgressVisible;
        public bool IsUpdateProgressVisible
        {
            get => _isUpdateProgressVisible;
            private set => SetProperty(ref _isUpdateProgressVisible, value);
        }

        private string? _updateErrorMessage;
        public string? UpdateErrorMessage
        {
            get => _updateErrorMessage;
            private set => SetProperty(ref _updateErrorMessage, value);
        }

        private bool _isUpdateBusy;
        public bool IsUpdateBusy
        {
            get => _isUpdateBusy;
            private set
            {
                if (SetProperty(ref _isUpdateBusy, value))
                {
                    CheckForUpdatesCommand?.RaiseCanExecuteChanged();
                }
            }
        }

        public override void Destroy()
        {
            _automationSettings.Changed -= HandleAutomationChanged;
            _matchService.SnapshotChanged -= HandleSnapshotChanged;
            _logHistory.EntryLogged -= HandleLogChanged;
            _logHistory.Cleared -= HandleLogChanged;
            _updateService.StateChanged -= HandleUpdateStateChanged;
            EventAggregator.GetEvent<LanguageSwitchedEvent>().Unsubscribe(RefreshConnectionStatus);
            EventAggregator.GetEvent<LanguageSwitchedEvent>().Unsubscribe(RefreshUpdateState);
            base.Destroy();
        }

        private void HandleAutomationChanged(object sender, EventArgs e)
        {
            Dispatch(() =>
            {
                RaisePropertyChanged(nameof(AutoAccept));
                RaisePropertyChanged(nameof(AutoReconnect));
            });
        }

        private void HandleSnapshotChanged(object sender, LiveMatchSnapshotChangedEventArgs args)
        {
            Dispatch(() => ConnectionState = args.Snapshot.ConnectionState);
        }

        private void HandleLogChanged(object sender, EventArgs args)
        {
            Dispatch(() => LogCount = _logHistory.GetSnapshot().Count);
        }

        private void HandleUpdateStateChanged(object sender, UpdateStateChangedEventArgs args)
        {
            Dispatch(RefreshUpdateState);
        }

        private async void CheckForUpdates()
        {
            var update = await _updateService.CheckAsync(true);
            if (update is not null)
            {
                Dispatch(() => _dialogService.ShowDialog(RegionNames.UpdateDialog));
            }
        }

        private void RefreshUpdateState()
        {
            var state = _updateService.State;
            IsUpdateBusy = state is UpdateState.Checking
                or UpdateState.Downloading or UpdateState.Installing;
            IsUpdateProgressVisible = state is UpdateState.Downloading
                or UpdateState.ReadyToInstall or UpdateState.Installing;
            UpdateProgress = _updateService.Progress * 100;
            UpdateErrorMessage = _updateService.ErrorMessage;
            var key = state switch
            {
                UpdateState.Checking => "Update.Status.Checking",
                UpdateState.UpToDate => "Update.Status.UpToDate",
                UpdateState.Available => "Update.Status.Available",
                UpdateState.Downloading => "Update.Status.Downloading",
                UpdateState.ReadyToInstall => "Update.Status.Ready",
                UpdateState.Installing => "Update.Status.Installing",
                UpdateState.Failed => "Update.Status.Failed",
                _ => "Update.Status.Idle"
            };
            UpdateStatus = ResourceService.FindResource<string>(key);
        }

        private void RefreshConnectionStatus()
        {
            var key = ConnectionState switch
            {
                ConnectionState.Connected => "Setting.Connection.Connected",
                ConnectionState.Connecting => "Setting.Connection.Connecting",
                ConnectionState.Reconnecting => "Setting.Connection.Reconnecting",
                ConnectionState.Stopping => "Setting.Connection.Stopping",
                ConnectionState.Error => "Setting.Connection.Error",
                _ => "Setting.Connection.Disconnected"
            };

            ConnectionStatus = ResourceService.FindResource<string>(key);
        }

        private static string GetApplicationVersion()
        {
            var assembly = Assembly.GetEntryAssembly();
            var informationalVersion = assembly?
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
                .InformationalVersion;

            if (!string.IsNullOrWhiteSpace(informationalVersion))
            {
                return informationalVersion.Split('+')[0];
            }

            return assembly?.GetName().Version?.ToString(3) ?? "1.0.0";
        }

        private static void Dispatch(Action action)
        {
            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher is null || dispatcher.CheckAccess())
            {
                action();
                return;
            }

            dispatcher.BeginInvoke(DispatcherPriority.Background, action);
        }
    }
}

using Prism.Events;
using Prometheus.Core.Events;
using Prometheus.Core.Models;
using Prometheus.Modules.Setting.Properties;
using Prometheus.Services.Interfaces;
using Prometheus.Services.Interfaces.Client;
using System;
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

        protected override string TitleResourceKey { get; set; } = "Setting.Personalization";

        public PreferenceViewModel(
            IEventAggregator eventAggregator,
            IResourceService resourceService,
            IGameAutomationSettings automationSettings,
            IMatchService matchService,
            ILogHistoryService logHistory)
            : base(eventAggregator, resourceService)
        {
            _automationSettings = automationSettings;
            _matchService = matchService;
            _logHistory = logHistory;

            _selectedLanguageIndex = Settings.Default.LanguageIndex;
            _selectedThemeIndex = Settings.Default.ThemeIndex;
            _connectionState = matchService.Current?.ConnectionState ?? ConnectionState.Disconnected;
            _logCount = logHistory.GetSnapshot().Count;

            ApplicationVersion = GetApplicationVersion();
            LogCapacity = logHistory.Capacity;
            RefreshConnectionStatus();

            automationSettings.Changed += HandleAutomationChanged;
            matchService.SnapshotChanged += HandleSnapshotChanged;
            logHistory.EntryLogged += HandleLogChanged;
            logHistory.Cleared += HandleLogChanged;
            EventAggregator.GetEvent<LanguageSwitchedEvent>().Subscribe(RefreshConnectionStatus);
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

        public override void Destroy()
        {
            _automationSettings.Changed -= HandleAutomationChanged;
            _matchService.SnapshotChanged -= HandleSnapshotChanged;
            _logHistory.EntryLogged -= HandleLogChanged;
            _logHistory.Cleared -= HandleLogChanged;
            EventAggregator.GetEvent<LanguageSwitchedEvent>().Unsubscribe(RefreshConnectionStatus);
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

using Prometheus.Services.Interfaces.Client;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json;

namespace Prometheus.Services.Client
{
    /// <summary>
    /// Small, fail-safe settings store for opt-in game automation.  Corrupt or
    /// unreadable files are treated as both switches being disabled.
    /// </summary>
    public sealed class GameAutomationSettings : IGameAutomationSettings
    {
        private static readonly Lazy<GameAutomationSettings> _default =
            new(() => new GameAutomationSettings());

        private readonly object _syncRoot = new();
        private readonly string _settingsPath;
        private bool _autoAcceptReadyCheck;
        private bool _autoReconnect;
        private bool _autoSwapAramBench;
        private bool _autoPickChampion;
        private bool _autoBanChampion;
        private int[] _preferredAramChampionIds = [];
        private int[] _preferredPickChampionIds = [];
        private int[] _preferredBanChampionIds = [];
        private bool _lastPersistenceSucceeded = true;

        public GameAutomationSettings()
            : this(GetDefaultSettingsPath())
        {
        }

        public GameAutomationSettings(string settingsPath)
        {
            if (string.IsNullOrWhiteSpace(settingsPath))
            {
                throw new ArgumentException("A settings file path is required.", nameof(settingsPath));
            }

            _settingsPath = Path.GetFullPath(settingsPath);
            Load();
        }

        public static GameAutomationSettings Default => _default.Value;

        public string SettingsPath => _settingsPath;

        public bool AutoAcceptReadyCheck
        {
            get
            {
                lock (_syncRoot)
                {
                    return _autoAcceptReadyCheck;
                }
            }
            set => SetAutoAccept(value);
        }

        public bool AutoReconnect
        {
            get
            {
                lock (_syncRoot)
                {
                    return _autoReconnect;
                }
            }
            set => SetAutoReconnect(value);
        }

        public bool AutoAccept
        {
            get => AutoAcceptReadyCheck;
            set => AutoAcceptReadyCheck = value;
        }

        public bool IsAutoAcceptEnabled
        {
            get => AutoAcceptReadyCheck;
            set => AutoAcceptReadyCheck = value;
        }

        public bool IsAutoReconnectEnabled
        {
            get => AutoReconnect;
            set => AutoReconnect = value;
        }

        public bool AutoSwapAramBench
        {
            get
            {
                lock (_syncRoot)
                {
                    return _autoSwapAramBench;
                }
            }
            set => SetAutoSwapAramBench(value);
        }

        public IReadOnlyList<int> PreferredAramChampionIds
        {
            get
            {
                lock (_syncRoot)
                {
                    return _preferredAramChampionIds.ToArray();
                }
            }
            set => SetPreferredAramChampionIds(value);
        }

        public bool AutoPickChampion
        {
            get
            {
                lock (_syncRoot)
                {
                    return _autoPickChampion;
                }
            }
            set => SetAutoPickChampion(value);
        }

        public bool AutoBanChampion
        {
            get
            {
                lock (_syncRoot)
                {
                    return _autoBanChampion;
                }
            }
            set => SetAutoBanChampion(value);
        }

        public IReadOnlyList<int> PreferredPickChampionIds
        {
            get
            {
                lock (_syncRoot)
                {
                    return _preferredPickChampionIds.ToArray();
                }
            }
            set => SetPreferredPickChampionIds(value);
        }

        public IReadOnlyList<int> PreferredBanChampionIds
        {
            get
            {
                lock (_syncRoot)
                {
                    return _preferredBanChampionIds.ToArray();
                }
            }
            set => SetPreferredBanChampionIds(value);
        }

        public bool LastPersistenceSucceeded
        {
            get
            {
                lock (_syncRoot)
                {
                    return _lastPersistenceSucceeded;
                }
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        public event EventHandler Changed;

        private static string GetDefaultSettingsPath()
        {
            var root = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            return Path.Combine(root, "Prometheus", "game-automation.json");
        }

        private void SetAutoAccept(bool value)
        {
            lock (_syncRoot)
            {
                if (_autoAcceptReadyCheck == value)
                {
                    return;
                }

                _autoAcceptReadyCheck = value;
            }

            UpdatePersistenceState(Save());
            RaisePropertyChanged(nameof(AutoAcceptReadyCheck));
            RaisePropertyChanged(nameof(AutoAccept));
            RaisePropertyChanged(nameof(IsAutoAcceptEnabled));
            Changed?.Invoke(this, EventArgs.Empty);
        }

        private void SetAutoReconnect(bool value)
        {
            lock (_syncRoot)
            {
                if (_autoReconnect == value)
                {
                    return;
                }

                _autoReconnect = value;
            }

            UpdatePersistenceState(Save());
            RaisePropertyChanged(nameof(AutoReconnect));
            RaisePropertyChanged(nameof(IsAutoReconnectEnabled));
            Changed?.Invoke(this, EventArgs.Empty);
        }

        private void SetAutoSwapAramBench(bool value)
        {
            lock (_syncRoot)
            {
                if (_autoSwapAramBench == value)
                {
                    return;
                }

                _autoSwapAramBench = value;
            }

            UpdatePersistenceState(Save());
            RaisePropertyChanged(nameof(AutoSwapAramBench));
            Changed?.Invoke(this, EventArgs.Empty);
        }

        private void SetPreferredAramChampionIds(IEnumerable<int> championIds)
        {
            var normalized = championIds?
                .Where(championId => championId > 0)
                .Distinct()
                .ToArray() ?? [];

            lock (_syncRoot)
            {
                if (_preferredAramChampionIds.SequenceEqual(normalized))
                {
                    return;
                }

                _preferredAramChampionIds = normalized;
            }

            UpdatePersistenceState(Save());
            RaisePropertyChanged(nameof(PreferredAramChampionIds));
            Changed?.Invoke(this, EventArgs.Empty);
        }

        private void SetAutoPickChampion(bool value)
        {
            lock (_syncRoot)
            {
                if (_autoPickChampion == value)
                {
                    return;
                }

                _autoPickChampion = value;
            }

            UpdatePersistenceState(Save());
            RaisePropertyChanged(nameof(AutoPickChampion));
            Changed?.Invoke(this, EventArgs.Empty);
        }

        private void SetAutoBanChampion(bool value)
        {
            lock (_syncRoot)
            {
                if (_autoBanChampion == value)
                {
                    return;
                }

                _autoBanChampion = value;
            }

            UpdatePersistenceState(Save());
            RaisePropertyChanged(nameof(AutoBanChampion));
            Changed?.Invoke(this, EventArgs.Empty);
        }

        private void SetPreferredPickChampionIds(IEnumerable<int> championIds)
        {
            var normalized = NormalizeChampionIds(championIds);
            lock (_syncRoot)
            {
                if (_preferredPickChampionIds.SequenceEqual(normalized))
                {
                    return;
                }

                _preferredPickChampionIds = normalized;
            }

            UpdatePersistenceState(Save());
            RaisePropertyChanged(nameof(PreferredPickChampionIds));
            Changed?.Invoke(this, EventArgs.Empty);
        }

        private void SetPreferredBanChampionIds(IEnumerable<int> championIds)
        {
            var normalized = NormalizeChampionIds(championIds);
            lock (_syncRoot)
            {
                if (_preferredBanChampionIds.SequenceEqual(normalized))
                {
                    return;
                }

                _preferredBanChampionIds = normalized;
            }

            UpdatePersistenceState(Save());
            RaisePropertyChanged(nameof(PreferredBanChampionIds));
            Changed?.Invoke(this, EventArgs.Empty);
        }

        private void Load()
        {
            try
            {
                if (!File.Exists(_settingsPath))
                {
                    return;
                }

                var json = File.ReadAllText(_settingsPath);
                var value = JsonSerializer.Deserialize<PersistedSettings>(json);
                if (value is null)
                {
                    return;
                }

                // Both values intentionally default to false when absent.
                _autoAcceptReadyCheck = value.AutoAcceptReadyCheck;
                _autoReconnect = value.AutoReconnect;
                _autoSwapAramBench = value.AutoSwapAramBench;
                _autoPickChampion = value.AutoPickChampion;
                _autoBanChampion = value.AutoBanChampion;
                _preferredAramChampionIds = NormalizeChampionIds(
                    value.PreferredAramChampionIds);
                _preferredPickChampionIds = NormalizeChampionIds(
                    value.PreferredPickChampionIds);
                _preferredBanChampionIds = NormalizeChampionIds(
                    value.PreferredBanChampionIds);
            }
            catch (IOException)
            {
                ResetToSafeDefaults();
            }
            catch (UnauthorizedAccessException)
            {
                ResetToSafeDefaults();
            }
            catch (JsonException)
            {
                ResetToSafeDefaults();
            }
        }

        private bool Save()
        {
            PersistedSettings value;
            lock (_syncRoot)
            {
                value = new PersistedSettings
                {
                    AutoAcceptReadyCheck = _autoAcceptReadyCheck,
                    AutoReconnect = _autoReconnect,
                    AutoSwapAramBench = _autoSwapAramBench,
                    AutoPickChampion = _autoPickChampion,
                    AutoBanChampion = _autoBanChampion,
                    PreferredAramChampionIds = _preferredAramChampionIds.ToArray(),
                    PreferredPickChampionIds = _preferredPickChampionIds.ToArray(),
                    PreferredBanChampionIds = _preferredBanChampionIds.ToArray()
                };
            }

            try
            {
                var directory = Path.GetDirectoryName(_settingsPath);
                if (!string.IsNullOrEmpty(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                var temporaryPath = _settingsPath + ".tmp";
                File.WriteAllText(temporaryPath, JsonSerializer.Serialize(value));
                File.Move(temporaryPath, _settingsPath, true);
                return true;
            }
            catch (IOException)
            {
                // Automation remains safe and usable for the current process;
                // a persistence failure must never enable a feature by itself.
                return false;
            }
            catch (UnauthorizedAccessException)
            {
                return false;
            }
        }

        private void UpdatePersistenceState(bool succeeded)
        {
            lock (_syncRoot)
            {
                _lastPersistenceSucceeded = succeeded;
            }
        }

        private static int[] NormalizeChampionIds(IEnumerable<int> championIds)
        {
            return championIds?
                .Where(championId => championId > 0)
                .Distinct()
                .ToArray() ?? [];
        }

        private void ResetToSafeDefaults()
        {
            _autoAcceptReadyCheck = false;
            _autoReconnect = false;
            _autoSwapAramBench = false;
            _autoPickChampion = false;
            _autoBanChampion = false;
            _preferredAramChampionIds = [];
            _preferredPickChampionIds = [];
            _preferredBanChampionIds = [];
        }

        private void RaisePropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        private sealed class PersistedSettings
        {
            public bool AutoAcceptReadyCheck { get; set; }

            public bool AutoReconnect { get; set; }

            public bool AutoSwapAramBench { get; set; }

            public bool AutoPickChampion { get; set; }

            public bool AutoBanChampion { get; set; }

            public int[] PreferredAramChampionIds { get; set; } = [];

            public int[] PreferredPickChampionIds { get; set; } = [];

            public int[] PreferredBanChampionIds { get; set; } = [];
        }
    }
}

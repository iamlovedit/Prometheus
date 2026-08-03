using Prometheus.Services.Interfaces.Client;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json;

namespace Prometheus.Services.Client
{
    public sealed class LcuCompanionSettings : ILcuCompanionSettings
    {
        private readonly object _syncRoot = new();
        private readonly string _settingsPath;
        private bool _isEnabled = true;
        private bool _lastPersistenceSucceeded = true;

        public LcuCompanionSettings()
            : this(GetDefaultSettingsPath())
        {
        }

        public LcuCompanionSettings(string settingsPath)
        {
            if (string.IsNullOrWhiteSpace(settingsPath))
            {
                throw new ArgumentException(
                    "A settings file path is required.", nameof(settingsPath));
            }

            _settingsPath = Path.GetFullPath(settingsPath);
            Load();
        }

        public string SettingsPath => _settingsPath;

        public bool IsEnabled
        {
            get
            {
                lock (_syncRoot)
                {
                    return _isEnabled;
                }
            }
            set => SetEnabled(value);
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

        private static string GetDefaultSettingsPath()
        {
            var root = Environment.GetFolderPath(
                Environment.SpecialFolder.LocalApplicationData);
            return Path.Combine(root, "Prometheus", "lcu-companion.json");
        }

        private void SetEnabled(bool value)
        {
            lock (_syncRoot)
            {
                if (_isEnabled == value)
                {
                    return;
                }

                _isEnabled = value;
            }

            UpdatePersistenceState(Save());
            RaisePropertyChanged(nameof(IsEnabled));
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
                if (value is not null)
                {
                    _isEnabled = value.IsEnabled;
                }
            }
            catch (IOException)
            {
                ResetToDefault();
            }
            catch (UnauthorizedAccessException)
            {
                ResetToDefault();
            }
            catch (JsonException)
            {
                ResetToDefault();
            }
        }

        private bool Save()
        {
            PersistedSettings value;
            lock (_syncRoot)
            {
                value = new PersistedSettings
                {
                    IsEnabled = _isEnabled
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

        private void ResetToDefault()
        {
            _isEnabled = true;
        }

        private void RaisePropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        private sealed class PersistedSettings
        {
            public bool IsEnabled { get; set; } = true;
        }
    }
}

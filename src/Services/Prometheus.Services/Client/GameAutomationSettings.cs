using Prometheus.Services.Interfaces.Client;
using System;
using System.ComponentModel;
using System.IO;
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

            Save();
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

            Save();
            RaisePropertyChanged(nameof(AutoReconnect));
            RaisePropertyChanged(nameof(IsAutoReconnectEnabled));
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
            }
            catch (IOException)
            {
                _autoAcceptReadyCheck = false;
                _autoReconnect = false;
            }
            catch (UnauthorizedAccessException)
            {
                _autoAcceptReadyCheck = false;
                _autoReconnect = false;
            }
            catch (JsonException)
            {
                _autoAcceptReadyCheck = false;
                _autoReconnect = false;
            }
        }

        private void Save()
        {
            PersistedSettings value;
            lock (_syncRoot)
            {
                value = new PersistedSettings
                {
                    AutoAcceptReadyCheck = _autoAcceptReadyCheck,
                    AutoReconnect = _autoReconnect
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
            }
            catch (IOException)
            {
                // Automation remains safe and usable for the current process;
                // a persistence failure must never enable a feature by itself.
            }
            catch (UnauthorizedAccessException)
            {
            }
        }

        private void RaisePropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        private sealed class PersistedSettings
        {
            public bool AutoAcceptReadyCheck { get; set; }

            public bool AutoReconnect { get; set; }
        }
    }
}

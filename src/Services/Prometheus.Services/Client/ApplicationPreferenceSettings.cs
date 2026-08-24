using Prometheus.Core.Models;
using Prometheus.Services.Interfaces.Client;
using System.Text.Json;

namespace Prometheus.Services.Client
{
    public sealed class ApplicationPreferenceSettings : IApplicationPreferenceSettings
    {
        private const string SettingsFileName = "application-preferences.json";

        private readonly object _syncRoot = new();
        private readonly string _settingsPath;
        private int? _languageIndex;
        private int? _themeIndex;
        private bool? _loggingEnabled;

        public ApplicationPreferenceSettings()
            : this(DefaultSettingsPath)
        {
        }

        public ApplicationPreferenceSettings(string settingsPath)
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

        public static string DefaultSettingsPath
        {
            get
            {
                var root = Environment.GetFolderPath(
                    Environment.SpecialFolder.LocalApplicationData);
                return Path.Combine(root, "Prometheus", SettingsFileName);
            }
        }

        public int? LanguageIndex
        {
            get
            {
                lock (_syncRoot)
                {
                    return _languageIndex;
                }
            }
        }

        public int? ThemeIndex
        {
            get
            {
                lock (_syncRoot)
                {
                    return _themeIndex;
                }
            }
        }

        public bool? LoggingEnabled
        {
            get
            {
                lock (_syncRoot)
                {
                    return _loggingEnabled;
                }
            }
        }

        public bool SaveLanguageIndex(int languageIndex)
        {
            if (languageIndex is < 0 or > 1)
            {
                throw new ArgumentOutOfRangeException(nameof(languageIndex));
            }

            lock (_syncRoot)
            {
                _languageIndex = languageIndex;
                return SaveLocked();
            }
        }

        public bool SaveThemeIndex(int themeIndex)
        {
            if (!Enum.IsDefined((ApplicationThemeMode)themeIndex))
            {
                throw new ArgumentOutOfRangeException(nameof(themeIndex));
            }

            lock (_syncRoot)
            {
                _themeIndex = themeIndex;
                return SaveLocked();
            }
        }

        public bool SaveLoggingEnabled(bool enabled)
        {
            lock (_syncRoot)
            {
                _loggingEnabled = enabled;
                return SaveLocked();
            }
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
                var settings = JsonSerializer.Deserialize<PersistedSettings>(json);
                if (settings is null)
                {
                    return;
                }

                lock (_syncRoot)
                {
                    _languageIndex = settings.LanguageIndex is >= 0 and <= 1
                        ? settings.LanguageIndex
                        : null;
                    _themeIndex = settings.ThemeIndex.HasValue
                        && Enum.IsDefined((ApplicationThemeMode)settings.ThemeIndex.Value)
                            ? settings.ThemeIndex
                            : null;
                    _loggingEnabled = settings.LoggingEnabled;
                }
            }
            catch (IOException)
            {
                Reset();
            }
            catch (UnauthorizedAccessException)
            {
                Reset();
            }
            catch (JsonException)
            {
                Reset();
            }
        }

        private bool SaveLocked()
        {
            try
            {
                var directory = Path.GetDirectoryName(_settingsPath);
                if (!string.IsNullOrWhiteSpace(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                var settings = new PersistedSettings
                {
                    LanguageIndex = _languageIndex,
                    ThemeIndex = _themeIndex,
                    LoggingEnabled = _loggingEnabled
                };
                var temporaryPath = _settingsPath + ".tmp";
                File.WriteAllText(temporaryPath, JsonSerializer.Serialize(settings));
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

        private void Reset()
        {
            lock (_syncRoot)
            {
                _languageIndex = null;
                _themeIndex = null;
                _loggingEnabled = null;
            }
        }

        private sealed class PersistedSettings
        {
            public int? LanguageIndex { get; set; }

            public int? ThemeIndex { get; set; }

            public bool? LoggingEnabled { get; set; }
        }
    }
}

using Prometheus.Core.Models;
using Prometheus.Services.Interfaces.Client;
using System;
using System.IO;
using System.Text.Json;

namespace Prometheus.Services.Client
{
    public sealed class ProfilePresentationSettings : IProfilePresentationSettings
    {
        private readonly object _syncRoot = new();
        private readonly string _settingsPath;

        private string _onlineStatus;
        private string _statusMessage;
        private QueueType? _queueType;
        private Tier? _tier;
        private Division? _division;

        public ProfilePresentationSettings()
            : this(GetDefaultSettingsPath())
        {
        }

        public ProfilePresentationSettings(string settingsPath)
        {
            if (string.IsNullOrWhiteSpace(settingsPath))
            {
                throw new ArgumentException("A settings file path is required.", nameof(settingsPath));
            }

            _settingsPath = Path.GetFullPath(settingsPath);
            Load();
        }

        public string SettingsPath => _settingsPath;

        public string OnlineStatus
        {
            get
            {
                lock (_syncRoot)
                {
                    return _onlineStatus;
                }
            }
        }

        public string StatusMessage
        {
            get
            {
                lock (_syncRoot)
                {
                    return _statusMessage;
                }
            }
        }

        public QueueType? QueueType
        {
            get
            {
                lock (_syncRoot)
                {
                    return _queueType;
                }
            }
        }

        public Tier? Tier
        {
            get
            {
                lock (_syncRoot)
                {
                    return _tier;
                }
            }
        }

        public Division? Division
        {
            get
            {
                lock (_syncRoot)
                {
                    return _division;
                }
            }
        }

        public void SaveOnlineStatus(string onlineStatus)
        {
            if (!IsSupportedOnlineStatus(onlineStatus))
            {
                throw new ArgumentOutOfRangeException(nameof(onlineStatus));
            }

            lock (_syncRoot)
            {
                _onlineStatus = onlineStatus;
            }

            Save();
        }

        public void SaveStatusMessage(string statusMessage)
        {
            lock (_syncRoot)
            {
                _statusMessage = statusMessage ?? string.Empty;
            }

            Save();
        }

        public void SaveTier(QueueType queueType, Tier tier, Division division)
        {
            if (!IsValidTier(queueType, tier, division))
            {
                throw new ArgumentOutOfRangeException(nameof(division));
            }

            lock (_syncRoot)
            {
                _queueType = queueType;
                _tier = tier;
                _division = division;
            }

            Save();
        }

        private static string GetDefaultSettingsPath()
        {
            var root = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            return Path.Combine(root, "Prometheus", "profile-presentation.json");
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

                lock (_syncRoot)
                {
                    _onlineStatus = IsSupportedOnlineStatus(value.OnlineStatus)
                        ? value.OnlineStatus
                        : null;
                    _statusMessage = value.StatusMessage;

                    if (value.QueueType.HasValue && value.Tier.HasValue && value.Division.HasValue &&
                        IsValidTier(value.QueueType.Value, value.Tier.Value, value.Division.Value))
                    {
                        _queueType = value.QueueType;
                        _tier = value.Tier;
                        _division = value.Division;
                    }
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

        private void Save()
        {
            PersistedSettings value;
            lock (_syncRoot)
            {
                value = new PersistedSettings
                {
                    OnlineStatus = _onlineStatus,
                    StatusMessage = _statusMessage,
                    QueueType = _queueType,
                    Tier = _tier,
                    Division = _division
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
            }
            catch (UnauthorizedAccessException)
            {
            }
        }

        private void Reset()
        {
            lock (_syncRoot)
            {
                _onlineStatus = null;
                _statusMessage = null;
                _queueType = null;
                _tier = null;
                _division = null;
            }
        }

        private static bool IsSupportedOnlineStatus(string value)
        {
            return value is "chat" or "away" or "offline";
        }

        private static bool IsValidTier(QueueType queueType, Tier tier, Division division)
        {
            if (!Enum.IsDefined(queueType) || !Enum.IsDefined(tier) || !Enum.IsDefined(division))
            {
                return false;
            }

            var hasDivision = tier is >= Prometheus.Core.Models.Tier.IRON
                and <= Prometheus.Core.Models.Tier.DIAMOND;
            return hasDivision
                ? division != Prometheus.Core.Models.Division.NA
                : division == Prometheus.Core.Models.Division.NA;
        }

        private sealed class PersistedSettings
        {
            public string OnlineStatus { get; set; }

            public string StatusMessage { get; set; }

            public QueueType? QueueType { get; set; }

            public Tier? Tier { get; set; }

            public Division? Division { get; set; }
        }
    }
}

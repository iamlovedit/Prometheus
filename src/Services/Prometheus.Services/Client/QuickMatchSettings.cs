using Prometheus.Core.Models;
using Prometheus.Services.Interfaces.Client;
using System.Text.Json;

namespace Prometheus.Services.Client
{
    public sealed class QuickMatchSettings : IQuickMatchSettings
    {
        private static readonly HashSet<int> SupportedQueueIds =
        [
            GameQueueIds.RankedSoloDuo,
            GameQueueIds.RankedFlex,
            GameQueueIds.Aram,
            GameQueueIds.HextechAram
        ];

        private readonly object _syncRoot = new();
        private readonly string _settingsPath;
        private int _queueId = GameQueueIds.RankedSoloDuo;

        public event EventHandler Changed;

        public QuickMatchSettings()
            : this(GetDefaultSettingsPath())
        {
        }

        public QuickMatchSettings(string settingsPath)
        {
            if (string.IsNullOrWhiteSpace(settingsPath))
            {
                throw new ArgumentException(
                    "A settings file path is required.", nameof(settingsPath));
            }

            _settingsPath = Path.GetFullPath(settingsPath);
            Load();
        }

        public int QueueId
        {
            get
            {
                lock (_syncRoot)
                {
                    return _queueId;
                }
            }
        }

        public bool SaveQueueId(int queueId)
        {
            if (!SupportedQueueIds.Contains(queueId))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(queueId), queueId, "Unsupported quick-match queue id.");
            }

            var changed = false;
            lock (_syncRoot)
            {
                changed = _queueId != queueId;
                _queueId = queueId;
            }

            var persisted = Save();
            if (changed)
            {
                Changed?.Invoke(this, EventArgs.Empty);
            }

            return persisted;
        }

        private static string GetDefaultSettingsPath()
        {
            var root = Environment.GetFolderPath(
                Environment.SpecialFolder.LocalApplicationData);
            return Path.Combine(root, "Prometheus", "quick-match.json");
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
                if (settings is null || !SupportedQueueIds.Contains(settings.QueueId))
                {
                    Reset();
                    return;
                }

                lock (_syncRoot)
                {
                    _queueId = settings.QueueId;
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

        private bool Save()
        {
            int queueId;
            lock (_syncRoot)
            {
                queueId = _queueId;
            }

            try
            {
                var directory = Path.GetDirectoryName(_settingsPath);
                if (!string.IsNullOrWhiteSpace(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                var temporaryPath = _settingsPath + ".tmp";
                File.WriteAllText(temporaryPath, JsonSerializer.Serialize(
                    new PersistedSettings { QueueId = queueId }));
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
                _queueId = GameQueueIds.RankedSoloDuo;
            }
        }

        private sealed class PersistedSettings
        {
            public int QueueId { get; set; }
        }
    }
}

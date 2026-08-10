using Prometheus.Core.Models;
using Prometheus.Services.Interfaces.Client;
using Serilog;

namespace Prometheus.Services.Client
{
    public sealed class ProfilePresentationStartupService : IProfilePresentationStartupService
    {
        private readonly object _syncRoot = new();
        private readonly IMatchService _matchService;
        private readonly IGameService _gameService;
        private readonly IProfilePresentationSettings _settings;

        private bool _started;
        private bool _wasConnected;

        public ProfilePresentationStartupService(
            IMatchService matchService,
            IGameService gameService,
            IProfilePresentationSettings settings)
        {
            _matchService = matchService ?? throw new ArgumentNullException(nameof(matchService));
            _gameService = gameService ?? throw new ArgumentNullException(nameof(gameService));
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        }

        public void Start()
        {
            lock (_syncRoot)
            {
                if (_started)
                {
                    return;
                }

                _started = true;
                _wasConnected = false;
                _matchService.SnapshotChanged += HandleSnapshotChanged;
            }

            ObserveConnection(_matchService.Current);
        }

        public void Stop()
        {
            lock (_syncRoot)
            {
                if (!_started)
                {
                    return;
                }

                _started = false;
                _wasConnected = false;
                _matchService.SnapshotChanged -= HandleSnapshotChanged;
            }
        }

        private void HandleSnapshotChanged(object sender, LiveMatchSnapshotChangedEventArgs args)
        {
            ObserveConnection(args.Snapshot);
        }

        private void ObserveConnection(LiveMatchSnapshot snapshot)
        {
            var isConnected = snapshot?.ConnectionState == ConnectionState.Connected;
            var shouldApply = false;

            lock (_syncRoot)
            {
                if (!_started)
                {
                    return;
                }

                shouldApply = isConnected && !_wasConnected;
                _wasConnected = isConnected;
            }

            if (shouldApply)
            {
                _ = ApplySavedSettingsAsync();
            }
        }

        private async Task ApplySavedSettingsAsync()
        {
            var onlineStatus = _settings.OnlineStatus;
            if (onlineStatus is not null)
            {
                await ApplySafelyAsync(
                    () => _gameService.SetOnlineStatusAsync(onlineStatus),
                    "online status");
            }

            var statusMessage = _settings.StatusMessage;
            if (statusMessage is not null)
            {
                await ApplySafelyAsync(
                    () => _gameService.SetStatusAsync(statusMessage),
                    "status message");
            }

            var queueType = _settings.QueueType;
            var tier = _settings.Tier;
            var division = _settings.Division;
            if (queueType.HasValue && tier.HasValue && division.HasValue)
            {
                await ApplySafelyAsync(
                    () => _gameService.SetChatTierAsync(
                        queueType.Value, tier.Value, division.Value),
                    "displayed rank");
            }
        }

        private static async Task ApplySafelyAsync(Func<Task> operation, string settingName)
        {
            try
            {
                await operation();
            }
            catch (Exception exception)
            {
                Log.Warning(exception,
                    "Unable to apply saved League profile {SettingName} after connection",
                    settingName);
            }
        }
    }
}

using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;
using Prometheus.Core.Logging;
using Prometheus.Core.Models;
using Prometheus.Services.Interfaces;
using Prometheus.Services.Interfaces.Client;
using Serilog;
using Serilog.Events;
using System.Collections.Concurrent;
using System.ComponentModel;
using System.Diagnostics;
using System.Net;
using System.Text;

namespace Prometheus.Services.Client
{
    /// <summary>
    /// Coordinates the LCU websocket lifecycle with cancellation-aware HTTP
    /// snapshots.  Every published snapshot is a replacement instance, making
    /// Current safe for UI readers without holding the service's locks.
    /// </summary>
    public partial class MatchService : IMatchService
    {
        private const int TeamSize = 5;
        private const int RecentMatchCount = 20;
        private const int MaximumConcurrentPlayerLoads = 4;

        private const string PhaseEndpoint = "lol-gameflow/v1/gameflow-phase";
        private const string SessionEndpoint = "lol-gameflow/v1/session";
        private const string LobbyEndpoint = "lol-lobby/v2/lobby";
        private const string MatchmakingEndpoint = "lol-matchmaking/v1/search";
        private const string ReadyCheckEndpoint = "lol-matchmaking/v1/ready-check";
        private const string ChampionSelectEndpoint = "lol-champ-select/v1/session";

        private static readonly string PhaseSubscriptionUri = ToWebsocketUri(PhaseEndpoint);
        private static readonly string SessionSubscriptionUri = ToWebsocketUri(SessionEndpoint);
        private static readonly string LobbySubscriptionUri = ToWebsocketUri(LobbyEndpoint);
        private static readonly string MatchmakingSubscriptionUri =
            ToWebsocketUri(MatchmakingEndpoint);
        private static readonly string ReadyCheckSubscriptionUri =
            ToWebsocketUri(ReadyCheckEndpoint);
        private static readonly string ChampionSelectSubscriptionUri =
            ToWebsocketUri(ChampionSelectEndpoint);

        private static readonly TimeSpan[] AcceptRetryDelays =
            [TimeSpan.Zero, TimeSpan.FromMilliseconds(500), TimeSpan.FromMilliseconds(1500)];

        private static readonly TimeSpan[] ReconnectRetryDelays =
            [TimeSpan.Zero, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(5)];

        private static readonly TimeSpan[] AramBenchSwapRetryDelays =
            [TimeSpan.Zero, TimeSpan.FromMilliseconds(300), TimeSpan.FromMilliseconds(900)];

        private static readonly TimeSpan[] AramBenchReadinessRefreshDelays =
        [
            TimeSpan.FromMilliseconds(250),
            TimeSpan.FromMilliseconds(750),
            TimeSpan.FromMilliseconds(1500),
            TimeSpan.FromMilliseconds(2500)
        ];

        private static readonly TimeSpan[] ChampionSelectActionRetryDelays =
            [TimeSpan.Zero, TimeSpan.FromMilliseconds(250), TimeSpan.FromMilliseconds(750)];
        private static readonly JsonSerializerSettings SnapshotCloneSettings = new()
        {
            ContractResolver = new WritablePropertiesContractResolver()
        };

        private readonly ILeagueClient _leagueClient;
        private readonly IHttpService _httpService;
        private readonly IGameService _gameService;
        private readonly ISummonerService _summonerService;
        private readonly IGameResourceManager _gameResourceManager;
        private readonly ILogger _logger;
        private readonly object _publicationSync = new();
        private readonly object _snapshotSync = new();
        private readonly object _stateSync = new();
        private readonly object _rosterSync = new();
        private readonly SemaphoreSlim _lifecycleGate = new(1, 1);
        private readonly SemaphoreSlim _connectionGate = new(1, 1);
        private readonly SemaphoreSlim _refreshGate = new(1, 1);
        private readonly SemaphoreSlim _playerLoadGate =
            new(MaximumConcurrentPlayerLoads, MaximumConcurrentPlayerLoads);
        private readonly ConcurrentDictionary<string,
            Lazy<Task<PlayerPerformanceData>>> _playerCache = new(StringComparer.Ordinal);
        private readonly ConcurrentDictionary<int, Lazy<Task<string>>>
            _recentChampionIconCache = new();

        private LiveMatchSnapshot _current = LiveMatchSnapshot.Empty;
        private CancellationTokenSource _lifetimeCts;
        private CancellationTokenSource _phaseCts;
        private CancellationTokenSource _playerLoadCts;
        private CancellationTokenSource _acceptAutomationCts;
        private CancellationTokenSource _reconnectAutomationCts;
        private CancellationTokenSource _aramBenchSwapAutomationCts;
        private CancellationTokenSource _championSelectAutomationCts;
        private Task _acceptAutomationTask = Task.CompletedTask;
        private Task _reconnectAutomationTask = Task.CompletedTask;
        private Task _aramBenchSwapAutomationTask = Task.CompletedTask;
        private Task _championSelectAutomationTask = Task.CompletedTask;
        private bool _started;
        private bool _subscribed;
        private long _phaseVersion;
        private long _phaseInstance;
        private long _lastAutoAcceptInstance = -1;
        private long _lastAutoReconnectInstance = -1;
        private string _lastAramBenchSwapState = string.Empty;
        private string _aramBenchSwapFailedState = string.Empty;
        private bool _aramBenchSwapFailureRetryConsumed;
        private string _lastChampionSelectAutomationState = string.Empty;
        private string _initializedConnection = string.Empty;
        private CancellationTokenSource _rosterCts;
        private Task _rosterTask = Task.CompletedTask;
        private string _rosterSourceSignature = string.Empty;
        private long _rosterGeneration;
        private long _snapshotVersion;
        private SummonerAccount _currentSummoner;

        public MatchService(ILeagueClient leagueClient, IHttpService httpService,
            IGameService gameService, ISummonerService summonerService,
            IGameResourceManager gameResourceManager,
            IGameAutomationSettings automationSettings = null)
            : this(leagueClient, httpService, gameService, summonerService,
                gameResourceManager, automationSettings, Log.ForContext<MatchService>())
        {
        }

        internal MatchService(ILeagueClient leagueClient, IHttpService httpService,
            IGameService gameService, ISummonerService summonerService,
            IGameResourceManager gameResourceManager,
            IGameAutomationSettings automationSettings, ILogger logger)
        {
            _leagueClient = leagueClient ?? throw new ArgumentNullException(nameof(leagueClient));
            _httpService = httpService ?? throw new ArgumentNullException(nameof(httpService));
            _gameService = gameService ?? throw new ArgumentNullException(nameof(gameService));
            _summonerService = summonerService ??
                throw new ArgumentNullException(nameof(summonerService));
            _gameResourceManager = gameResourceManager ??
                throw new ArgumentNullException(nameof(gameResourceManager));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            AutomationSettings = automationSettings ?? GameAutomationSettings.Default;
        }

        public LiveMatchSnapshot Current
        {
            get
            {
                LiveMatchSnapshot current;
                lock (_snapshotSync)
                {
                    current = _current;
                }

                return CloneForConsumer(current);
            }
        }

        public IGameAutomationSettings AutomationSettings { get; }

        public event EventHandler<LiveMatchSnapshotChangedEventArgs> SnapshotChanged;

        private LiveMatchSnapshot GetCurrentSnapshot()
        {
            lock (_snapshotSync)
            {
                return _current;
            }
        }

        private static LiveMatchSnapshot CloneForConsumer(LiveMatchSnapshot source)
        {
            if (source is null)
            {
                return LiveMatchSnapshot.Empty;
            }

            return DeserializeForConsumer(SerializeForConsumer(source));
        }

        private static string SerializeForConsumer(LiveMatchSnapshot source)
        {
            return JsonConvert.SerializeObject(source, SnapshotCloneSettings);
        }

        private static LiveMatchSnapshot DeserializeForConsumer(string json)
        {
            return JsonConvert.DeserializeObject<LiveMatchSnapshot>(json,
                SnapshotCloneSettings) ?? LiveMatchSnapshot.Empty;
        }

        public async Task StartAsync(CancellationToken cancellationToken = default)
        {
            CancellationToken lifetimeToken;
            await _lifecycleGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                if (_started)
                {
                    return;
                }

                _started = true;
                _lifetimeCts = new CancellationTokenSource();
                _phaseCts = CancellationTokenSource.CreateLinkedTokenSource(_lifetimeCts.Token);
                _playerLoadCts = CancellationTokenSource.CreateLinkedTokenSource(
                    _lifetimeCts.Token);
                _phaseVersion++;
                _phaseInstance++;
                _initializedConnection = string.Empty;
                _lastAramBenchSwapState = string.Empty;
                _lastChampionSelectAutomationState = string.Empty;
                AttachSubscriptions();
                AutomationSettings.PropertyChanged += HandleAutomationSettingsChanged;
                lifetimeToken = _lifetimeCts.Token;
            }
            finally
            {
                _lifecycleGate.Release();
            }

            _ = CancelRosterEnrichment(clearCache: true, resetSignature: true);
            _currentSummoner = null;

            PublishSnapshot(snapshot =>
            {
                var next = CopySnapshot(snapshot);
                next.ConnectionState = ConnectionState.Connecting;
                next.DataQuality = snapshot.DataQuality == DataQuality.Unknown
                    ? DataQuality.Unknown
                    : DataQuality.Stale;
                next.Error = string.Empty;
                next.Errors = Array.Empty<string>();
                return next;
            });

            try
            {
                var connected = await _leagueClient.StartAsync(cancellationToken).ConfigureAwait(false);
                if (connected)
                {
                    await HandleConnectedAsync(lifetimeToken).ConfigureAwait(false);
                }
                else
                {
                    PublishSnapshot(snapshot =>
                    {
                        var next = CopySnapshot(snapshot);
                        next.ConnectionState = ConnectionState.Disconnected;
                        next.DataQuality = snapshot.DataQuality == DataQuality.Unknown
                            ? DataQuality.Unknown
                            : DataQuality.Stale;
                        next.Error = string.Empty;
                        next.Errors = Array.Empty<string>();
                        return next;
                    });
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                await StopAsync().ConfigureAwait(false);
                throw;
            }
            catch (Exception exception)
            {
                PublishError("Unable to start the League client connection.", exception, true);
            }
        }

        public async Task RefreshAsync(CancellationToken cancellationToken = default)
        {
            CancellationToken lifetimeToken;
            long phaseVersionBefore;
            lock (_stateSync)
            {
                if (!_started || _lifetimeCts is null)
                {
                    return;
                }

                lifetimeToken = _lifetimeCts.Token;
                phaseVersionBefore = _phaseVersion;
            }

            // RefreshAsync is the user-visible/manual refresh boundary. Automatic
            // websocket updates reuse both completed and in-flight player loads;
            // an explicit refresh cancels that lifetime and starts a clean load.
            _ = CancelRosterEnrichment(clearCache: false, resetSignature: true);
            ResetPlayerLoadLifetime();

            if (!_leagueClient.Connected || !_httpService.IsInitialized)
            {
                PublishSnapshot(snapshot =>
                {
                    var next = CopySnapshot(snapshot);
                    next.ConnectionState = _leagueClient.Connected
                        ? ConnectionState.Connecting
                        : ConnectionState.Reconnecting;
                    next.DataQuality = snapshot.DataQuality == DataQuality.Unknown
                        ? DataQuality.Unknown
                        : DataQuality.Stale;
                    next.Error = "The League client connection is not ready.";
                    next.Errors = [next.Error];
                    return next;
                });
                return;
            }

            using var probeCts = CancellationTokenSource.CreateLinkedTokenSource(
                lifetimeToken, cancellationToken);

            string rawPhase = null;
            string phaseError = null;
            Exception phaseException = null;
            try
            {
                rawPhase = await _gameService.GetGameflowPhaseAsync(probeCts.Token)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (probeCts.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                phaseException = exception;
                phaseError = FormatError("gameflow-phase", exception);
            }

            if (!_leagueClient.Connected || !_httpService.IsInitialized)
            {
                return;
            }

            // A websocket phase change that happened while the GET was in
            // flight is newer than the HTTP response.  Never transition back.
            lock (_stateSync)
            {
                if (phaseVersionBefore != _phaseVersion)
                {
                    return;
                }
            }

            var context = string.IsNullOrWhiteSpace(rawPhase)
                ? GetCurrentPhaseContext()
                : TransitionPhase(rawPhase, scheduleRosterRefresh: false).Context;

            await RefreshForPhaseAsync(context, phaseError, phaseException, cancellationToken,
                    forceRosterReload: true)
                .ConfigureAwait(false);
        }

        public Task StopAsync()
        {
            return StopAsync(CancellationToken.None);
        }

        public async Task StopAsync(CancellationToken cancellationToken)
        {
            CancellationTokenSource lifetimeCts;
            CancellationTokenSource phaseCts;
            CancellationTokenSource playerLoadCts;
            CancellationTokenSource acceptCts;
            CancellationTokenSource reconnectCts;
            CancellationTokenSource aramBenchSwapCts;
            CancellationTokenSource championSelectCts;
            Task acceptTask;
            Task reconnectTask;
            Task aramBenchSwapTask;
            Task championSelectTask;

            await _lifecycleGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                if (!_started)
                {
                    return;
                }

                _started = false;
                AutomationSettings.PropertyChanged -= HandleAutomationSettingsChanged;
                DetachSubscriptions();

                lock (_stateSync)
                {
                    lifetimeCts = _lifetimeCts;
                    phaseCts = _phaseCts;
                    playerLoadCts = _playerLoadCts;
                    acceptCts = _acceptAutomationCts;
                    reconnectCts = _reconnectAutomationCts;
                    aramBenchSwapCts = _aramBenchSwapAutomationCts;
                    championSelectCts = _championSelectAutomationCts;
                    acceptTask = _acceptAutomationTask;
                    reconnectTask = _reconnectAutomationTask;
                    aramBenchSwapTask = _aramBenchSwapAutomationTask;
                    championSelectTask = _championSelectAutomationTask;
                    _lifetimeCts = null;
                    _phaseCts = null;
                    _playerLoadCts = null;
                    _acceptAutomationCts = null;
                    _reconnectAutomationCts = null;
                    _aramBenchSwapAutomationCts = null;
                    _championSelectAutomationCts = null;
                    _lastAramBenchSwapState = string.Empty;
                    _aramBenchSwapFailedState = string.Empty;
                    _aramBenchSwapFailureRetryConsumed = false;
                    _lastChampionSelectAutomationState = string.Empty;
                    _phaseVersion++;
                    _initializedConnection = string.Empty;
                }
            }
            finally
            {
                _lifecycleGate.Release();
            }

            PublishSnapshot(snapshot =>
            {
                var next = CopySnapshot(snapshot);
                next.ConnectionState = ConnectionState.Stopping;
                next.DataQuality = snapshot.DataQuality == DataQuality.Unknown
                    ? DataQuality.Unknown
                    : DataQuality.Stale;
                return next;
            });

            lifetimeCts?.Cancel();
            phaseCts?.Cancel();
            playerLoadCts?.Cancel();
            acceptCts?.Cancel();
            reconnectCts?.Cancel();
            aramBenchSwapCts?.Cancel();
            championSelectCts?.Cancel();
            var rosterTask = CancelRosterEnrichment(clearCache: true, resetSignature: true);
            _currentSummoner = null;
            _httpService.Reset();

            await _leagueClient.StopAsync(cancellationToken).ConfigureAwait(false);
            await AwaitAutomationTaskAsync(acceptTask).ConfigureAwait(false);
            await AwaitAutomationTaskAsync(reconnectTask).ConfigureAwait(false);
            await AwaitAutomationTaskAsync(aramBenchSwapTask).ConfigureAwait(false);
            await AwaitAutomationTaskAsync(championSelectTask).ConfigureAwait(false);
            await AwaitAutomationTaskAsync(rosterTask).ConfigureAwait(false);

            lifetimeCts?.Dispose();
            phaseCts?.Dispose();
            playerLoadCts?.Dispose();
            acceptCts?.Dispose();
            reconnectCts?.Dispose();
            aramBenchSwapCts?.Dispose();
            championSelectCts?.Dispose();

            PublishSnapshot(snapshot =>
            {
                var next = CopySnapshot(snapshot);
                next.ConnectionState = ConnectionState.Disconnected;
                next.DataQuality = snapshot.DataQuality == DataQuality.Unknown
                    ? DataQuality.Unknown
                    : DataQuality.Stale;
                next.Error = string.Empty;
                next.Errors = Array.Empty<string>();
                return next;
            });
        }

        public Task AcceptReadyCheckAsync(CancellationToken cancellationToken = default)
        {
            return _gameService.AcceptMatchAsync(cancellationToken);
        }

        public Task ReconnectAsync(CancellationToken cancellationToken = default)
        {
            return _gameService.ReconnectGameAsync(cancellationToken);
        }

        private void AttachSubscriptions()
        {
            if (_subscribed)
            {
                return;
            }

            // This method is deliberately called before LeagueClient.StartAsync
            // and therefore before any startup GET.
            _leagueClient.OnConnected += HandleLeagueConnected;
            _leagueClient.OnDisconnected += HandleLeagueDisconnected;
            _leagueClient.Subscribe(PhaseSubscriptionUri, HandlePhaseEvent);
            _leagueClient.Subscribe(SessionSubscriptionUri, HandleSessionEvent);
            _leagueClient.Subscribe(LobbySubscriptionUri, HandleLobbyEvent);
            _leagueClient.Subscribe(MatchmakingSubscriptionUri, HandleMatchmakingEvent);
            _leagueClient.Subscribe(ReadyCheckSubscriptionUri, HandleReadyCheckEvent);
            _leagueClient.Subscribe(ChampionSelectSubscriptionUri,
                HandleChampionSelectEvent);
            _subscribed = true;
        }

        private void DetachSubscriptions()
        {
            if (!_subscribed)
            {
                return;
            }

            _leagueClient.OnConnected -= HandleLeagueConnected;
            _leagueClient.OnDisconnected -= HandleLeagueDisconnected;
            _leagueClient.Unsubscribe(PhaseSubscriptionUri, HandlePhaseEvent);
            _leagueClient.Unsubscribe(SessionSubscriptionUri, HandleSessionEvent);
            _leagueClient.Unsubscribe(LobbySubscriptionUri, HandleLobbyEvent);
            _leagueClient.Unsubscribe(MatchmakingSubscriptionUri,
                HandleMatchmakingEvent);
            _leagueClient.Unsubscribe(ReadyCheckSubscriptionUri,
                HandleReadyCheckEvent);
            _leagueClient.Unsubscribe(ChampionSelectSubscriptionUri,
                HandleChampionSelectEvent);
            _subscribed = false;
        }

        private void HandleLeagueConnected()
        {
            CancellationToken token;
            lock (_stateSync)
            {
                if (!_started || _lifetimeCts is null)
                {
                    return;
                }

                token = _lifetimeCts.Token;
            }

            _ = HandleConnectedSafelyAsync(token);
        }

        private async Task HandleConnectedSafelyAsync(CancellationToken cancellationToken)
        {
            try
            {
                await HandleConnectedAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
            }
            catch (Exception exception)
            {
                PublishError("Unable to initialize the League client HTTP connection.", exception, true);
            }
        }

        private async Task HandleConnectedAsync(CancellationToken cancellationToken)
        {
            await _connectionGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                lock (_stateSync)
                {
                    if (!_started)
                    {
                        return;
                    }
                }

                if (!_leagueClient.Connected)
                {
                    return;
                }

                if (!int.TryParse(_leagueClient.Port, out var port))
                {
                    throw new InvalidOperationException("The League client supplied an invalid HTTP port.");
                }

                var connectionId = $"{_leagueClient.ProcessId}:{_leagueClient.Port}:{_leagueClient.Token}";
                if (string.Equals(_initializedConnection, connectionId, StringComparison.Ordinal) &&
                    GetCurrentSnapshot().ConnectionState == ConnectionState.Connected)
                {
                    return;
                }

                // Connected is not published until this succeeds.
                _httpService.Initialize(port, _leagueClient.Token);
                if (!_leagueClient.Connected)
                {
                    _httpService.Reset();
                    return;
                }

                _initializedConnection = connectionId;

                PublishSnapshot(snapshot =>
                {
                    var next = CopySnapshot(snapshot);
                    if (!_leagueClient.Connected || !_httpService.IsInitialized)
                    {
                        next.ConnectionState = ConnectionState.Reconnecting;
                        next.DataQuality = snapshot.DataQuality == DataQuality.Unknown
                            ? DataQuality.Unknown
                            : DataQuality.Stale;
                        next.Error = "The League client disconnected; reconnecting.";
                        next.Errors = [next.Error];
                        return next;
                    }

                    next.ConnectionState = ConnectionState.Connected;
                    next.DataQuality = snapshot.DataQuality == DataQuality.Stale
                        ? DataQuality.Partial
                        : snapshot.DataQuality;
                    next.Error = string.Empty;
                    next.Errors = Array.Empty<string>();
                    return next;
                });

                await RefreshAsync(cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                _connectionGate.Release();
            }
        }

        private void HandleLeagueDisconnected()
        {
            CancellationTokenSource phaseCts;
            CancellationTokenSource acceptCts;
            CancellationTokenSource reconnectCts;
            CancellationTokenSource aramBenchSwapCts;
            lock (_stateSync)
            {
                if (!_started)
                {
                    return;
                }

                phaseCts = _phaseCts;
                acceptCts = _acceptAutomationCts;
                reconnectCts = _reconnectAutomationCts;
                aramBenchSwapCts = _aramBenchSwapAutomationCts;
                _phaseCts = CancellationTokenSource.CreateLinkedTokenSource(_lifetimeCts.Token);
                _phaseVersion++;
                _phaseInstance++;
                _initializedConnection = string.Empty;
                _lastAramBenchSwapState = string.Empty;
                _aramBenchSwapFailedState = string.Empty;
                _aramBenchSwapFailureRetryConsumed = false;
            }

            phaseCts?.Cancel();
            acceptCts?.Cancel();
            reconnectCts?.Cancel();
            aramBenchSwapCts?.Cancel();
            CancelRosterEnrichment(clearCache: false, resetSignature: true);
            ResetPlayerLoadLifetime();
            _currentSummoner = null;
            _httpService.Reset();

            PublishSnapshot(snapshot =>
            {
                var next = CopySnapshot(snapshot);
                next.ConnectionState = ConnectionState.Reconnecting;
                next.DataQuality = snapshot.DataQuality == DataQuality.Unknown
                    ? DataQuality.Unknown
                    : DataQuality.Stale;
                next.Error = "The League client disconnected; reconnecting.";
                next.Errors = [next.Error];
                return next;
            });
        }

        private void HandlePhaseEvent(OnWebsocketEventArgs args)
        {
            var rawPhase = ReadPhase(args?.Data);
            if (string.IsNullOrWhiteSpace(rawPhase))
            {
                return;
            }

            var transition = TransitionPhase(rawPhase);
            if (transition.Changed)
            {
                _ = RefreshForPhaseSafelyAsync(transition.Context);
            }
        }

        private async Task RefreshForPhaseSafelyAsync(PhaseContext context)
        {
            try
            {
                await RefreshForPhaseAsync(context, null, null, CancellationToken.None)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (context.Token.IsCancellationRequested)
            {
            }
            catch (Exception exception)
            {
                PublishError("Unable to refresh the current gameflow phase.", exception, false);
            }
        }

        private void HandleSessionEvent(OnWebsocketEventArgs args)
        {
            var value = IsDelete(args) ? null : DeserializeEvent<GameflowSessionSnapshot>(args?.Data);
            PublishResourceUpdate(snapshot =>
            {
                snapshot.GameflowSession = value;
                return snapshot;
            });

            if (!string.IsNullOrWhiteSpace(value?.Phase) &&
                !string.Equals(value.Phase, GetCurrentSnapshot().RawPhase,
                    StringComparison.OrdinalIgnoreCase))
            {
                var transition = TransitionPhase(value.Phase);
                if (transition.Changed)
                {
                    _ = RefreshForPhaseSafelyAsync(transition.Context);
                }
                return;
            }

            ScheduleRosterRefresh();
            TriggerAutomation(GetCurrentPhaseContext());
        }

        private void HandleLobbyEvent(OnWebsocketEventArgs args)
        {
            var value = IsDelete(args) ? null : DeserializeEvent<LobbySnapshot>(args?.Data);
            PublishResourceUpdate(snapshot =>
            {
                snapshot.Lobby = value;
                return snapshot;
            });
            TriggerAutomation(GetCurrentPhaseContext());
        }

        private void HandleMatchmakingEvent(OnWebsocketEventArgs args)
        {
            var value = IsDelete(args) ? null : DeserializeEvent<MatchmakingSnapshot>(args?.Data);
            PublishResourceUpdate(snapshot =>
            {
                snapshot.Matchmaking = value;
                return snapshot;
            });
            TriggerAutomation(GetCurrentPhaseContext());
        }

        private void HandleReadyCheckEvent(OnWebsocketEventArgs args)
        {
            var value = IsDelete(args) ? null : DeserializeEvent<ReadyCheckSnapshot>(args?.Data);
            PublishResourceUpdate(snapshot =>
            {
                snapshot.ReadyCheck = value;
                return snapshot;
            });

            TriggerAutomation(GetCurrentPhaseContext());
        }

        private void HandleChampionSelectEvent(OnWebsocketEventArgs args)
        {
            var value = IsDelete(args) ? null : DeserializeEvent<ChampionSelectSnapshot>(args?.Data);
            NormalizeChampionSelect(value);
            PublishResourceUpdate(snapshot =>
            {
                snapshot.ChampionSelect = value;
                return snapshot;
            });
            ScheduleRosterRefresh();
            TriggerAutomation(GetCurrentPhaseContext());
        }

        private async Task RefreshForPhaseAsync(PhaseContext context, string phaseError,
            Exception phaseException, CancellationToken cancellationToken,
            bool forceRosterReload = false)
        {
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
                context.Token, cancellationToken);
            var token = linkedCts.Token;

            await _refreshGate.WaitAsync(token).ConfigureAwait(false);
            try
            {
                if (!IsCurrentPhase(context) || !_leagueClient.Connected ||
                    !_httpService.IsInitialized)
                {
                    return;
                }

                var sessionTask = FetchAsync("gameflow-session",
                    () => _gameService.GetGameflowSessionSnapshotAsync(token), token);
                var lobbyTask = FetchAsync("lobby",
                    () => _gameService.GetLobbySnapshotAsync(token), token);
                var matchmakingTask = FetchAsync("matchmaking",
                    () => _gameService.GetMatchmakingSnapshotAsync(token), token);
                var readyCheckTask = FetchAsync("ready-check",
                    () => _gameService.GetReadyCheckSnapshotAsync(token), token);
                var championSelectTask = FetchAsync("champion-select",
                    () => _gameService.GetChampionSelectSnapshotAsync(token), token);
                var postGameTask = IsPostGamePhase(context.Phase)
                    ? FetchPostGameAsync(context, token)
                    : Task.FromResult(FetchResult<PostGameSnapshot>.Unavailable());

                await Task.WhenAll(sessionTask, lobbyTask, matchmakingTask,
                    readyCheckTask, championSelectTask, postGameTask).ConfigureAwait(false);

                if (!IsCurrentPhase(context) || !_leagueClient.Connected ||
                    !_httpService.IsInitialized)
                {
                    return;
                }

                var session = await sessionTask.ConfigureAwait(false);
                var lobby = await lobbyTask.ConfigureAwait(false);
                var matchmaking = await matchmakingTask.ConfigureAwait(false);
                var readyCheck = await readyCheckTask.ConfigureAwait(false);
                var championSelect = await championSelectTask.ConfigureAwait(false);
                var postGame = await postGameTask.ConfigureAwait(false);
                NormalizeChampionSelect(championSelect.Value);

                var errors = new List<string>();
                AddError(errors, phaseError);
                AddError(errors, session.Error);
                AddError(errors, lobby.Error);
                AddError(errors, matchmaking.Error);
                AddError(errors, readyCheck.Error);
                AddError(errors, championSelect.Error);
                AddError(errors, postGame.Error);

                var failedResources = new List<string>();
                var failureExceptions = new List<Exception>();
                AddRefreshFailure(failedResources, failureExceptions,
                    "gameflow-phase", phaseException);
                AddRefreshFailure(failedResources, failureExceptions,
                    "gameflow-session", session.Exception);
                AddRefreshFailure(failedResources, failureExceptions,
                    "lobby", lobby.Exception);
                AddRefreshFailure(failedResources, failureExceptions,
                    "matchmaking", matchmaking.Exception);
                AddRefreshFailure(failedResources, failureExceptions,
                    "ready-check", readyCheck.Exception);
                AddRefreshFailure(failedResources, failureExceptions,
                    "champion-select", championSelect.Exception);
                AddRefreshFailure(failedResources, failureExceptions,
                    "post-game", postGame.Exception);
                var errorLogContext = errors.Count == 0
                    ? null
                    : new SnapshotErrorLogContext(
                        failedResources.Count == 0
                            ? "Live-match snapshot refresh reported an error."
                            : $"Unable to refresh live-match resources: " +
                              $"{string.Join(", ", failedResources)}.",
                        failureExceptions);

                PublishSnapshot(snapshot =>
                {
                    var next = CopySnapshot(snapshot);
                    if (!session.Failed)
                    {
                        next.GameflowSession = session.Value;
                    }

                    if (!lobby.Failed)
                    {
                        next.Lobby = lobby.Value;
                    }

                    if (!matchmaking.Failed)
                    {
                        next.Matchmaking = matchmaking.Value;
                    }

                    if (!readyCheck.Failed)
                    {
                        next.ReadyCheck = readyCheck.Value;
                    }

                    if (!championSelect.Failed)
                    {
                        next.ChampionSelect = championSelect.Value;
                    }

                    if (IsPostGamePhase(context.Phase) && !postGame.Failed &&
                        postGame.Value is not null)
                    {
                        next.PostGame = postGame.Value;
                    }

                    next.ConnectionState = ConnectionState.Connected;
                    next.Errors = errors;
                    next.Error = errors.Count == 0 ? string.Empty : string.Join("; ", errors);
                    next.DataQuality = errors.Count == 0
                        ? DataQuality.Complete
                        : HasAnyData(next) ? DataQuality.Partial : DataQuality.Error;
                    ApplyRosterDataQuality(next);
                    return next;
                }, errorLogContext);
                if (postGame.Value is not null)
                {
                    SchedulePostGameChampionIconEnrichment(postGame.Value);
                }
                ScheduleRosterRefresh(forceRosterReload);
                TriggerAutomation(context);
            }
            finally
            {
                _refreshGate.Release();
            }
        }

        private static string NormalizePuuid(string puuid)
        {
            return puuid?.Trim().ToUpperInvariant() ?? string.Empty;
        }

        private static string ToWebsocketUri(string endpoint)
        {
            return $"/{endpoint?.TrimStart('/') ?? string.Empty}";
        }

        private static string FirstNotEmpty(params string[] values)
        {
            return values?.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ??
                string.Empty;
        }

        private static string FormatRiotId(string gameName, string tagLine)
        {
            if (string.IsNullOrWhiteSpace(gameName))
            {
                return string.Empty;
            }

            return string.IsNullOrWhiteSpace(tagLine)
                ? gameName
                : $"{gameName}#{tagLine}";
        }

        private static bool TryGetCompleteRiotId(string value, out string riotId)
        {
            riotId = value?.Trim() ?? string.Empty;
            var separator = riotId.LastIndexOf('#');
            return separator > 0 && separator < riotId.Length - 1 &&
                !string.IsNullOrWhiteSpace(riotId[..separator]) &&
                !string.IsNullOrWhiteSpace(riotId[(separator + 1)..]);
        }

        private static string FormatSummonerName(SummonerAccount summoner,
            string fallback)
        {
            var name = FirstNotEmpty(summoner?.GameName, summoner?.DisplayName,
                summoner?.SummonerName);
            if (string.IsNullOrWhiteSpace(name))
            {
                return fallback ?? string.Empty;
            }
            return string.IsNullOrWhiteSpace(summoner?.TagLine)
                ? name
                : $"{name}#{summoner.TagLine}";
        }

        private static long GetGameId(LiveMatchSnapshot snapshot)
        {
            if (snapshot is null)
            {
                return 0;
            }

            if (snapshot.GameflowPhase == GameflowPhase.ChampSelect &&
                (snapshot.ChampionSelect?.GameId ?? 0) > 0)
            {
                return snapshot.ChampionSelect.GameId;
            }
            if (snapshot.GameflowPhase is GameflowPhase.GameStart or
                    GameflowPhase.InProgress or GameflowPhase.Reconnect &&
                (snapshot.GameflowSession?.GameData?.GameId ?? 0) > 0)
            {
                return snapshot.GameflowSession.GameData.GameId;
            }
            if (IsPostGamePhase(snapshot.GameflowPhase) &&
                (snapshot.PostGame?.GameId ?? 0) > 0)
            {
                return snapshot.PostGame.GameId;
            }
            if ((snapshot.GameflowSession?.GameData?.GameId ?? 0) > 0)
            {
                return snapshot.GameflowSession.GameData.GameId;
            }
            if ((snapshot.ChampionSelect?.GameId ?? 0) > 0)
            {
                return snapshot.ChampionSelect.GameId;
            }
            return snapshot.PostGame?.GameId ?? 0;
        }

        private static bool IsRosterPhase(GameflowPhase phase)
        {
            return phase is GameflowPhase.ChampSelect or GameflowPhase.GameStart or
                GameflowPhase.InProgress or GameflowPhase.Reconnect;
        }

        private PhaseTransitionResult TransitionPhase(string rawPhase,
            bool scheduleRosterRefresh = true)
        {
            rawPhase = rawPhase?.Trim().Trim('"') ?? string.Empty;
            var parsedPhase = ParsePhase(rawPhase);
            CancellationTokenSource oldPhaseCts = null;
            PhaseContext context;

            lock (_stateSync)
            {
                if (!_started || _lifetimeCts is null)
                {
                    return new PhaseTransitionResult(
                        new PhaseContext(_phaseVersion, _phaseInstance, parsedPhase,
                            rawPhase, new CancellationToken(true)), false);
                }

                if (string.Equals(GetCurrentSnapshot().RawPhase, rawPhase,
                        StringComparison.OrdinalIgnoreCase) &&
                    _phaseCts is not null)
                {
                    return new PhaseTransitionResult(
                        new PhaseContext(_phaseVersion, _phaseInstance, parsedPhase,
                            rawPhase, _phaseCts.Token), false);
                }

                oldPhaseCts = _phaseCts;
                _phaseCts = CancellationTokenSource.CreateLinkedTokenSource(_lifetimeCts.Token);
                _phaseVersion++;
                _phaseInstance++;
                context = new PhaseContext(_phaseVersion, _phaseInstance, parsedPhase,
                    rawPhase, _phaseCts.Token);
            }

            oldPhaseCts?.Cancel();
            oldPhaseCts?.Dispose();

            if (!IsRosterPhase(parsedPhase))
            {
                CancelRosterEnrichment(clearCache: false, resetSignature: true);
                ResetPlayerLoadLifetime();
            }

            PublishSnapshot(snapshot => PrepareForPhase(snapshot, parsedPhase, rawPhase));
            if (scheduleRosterRefresh)
            {
                ScheduleRosterRefresh();
            }
            TriggerAutomation(context);
            return new PhaseTransitionResult(context, true);
        }

        private PhaseContext GetCurrentPhaseContext()
        {
            lock (_stateSync)
            {
                var current = GetCurrentSnapshot();
                return new PhaseContext(_phaseVersion, _phaseInstance, current.GameflowPhase,
                    current.RawPhase, _phaseCts?.Token ?? new CancellationToken(true));
            }
        }

        private bool IsCurrentPhase(PhaseContext context)
        {
            lock (_stateSync)
            {
                return _started && context.Version == _phaseVersion &&
                       !context.Token.IsCancellationRequested;
            }
        }

    }
}

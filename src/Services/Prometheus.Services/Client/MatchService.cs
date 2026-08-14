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
    public class MatchService : IMatchService
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

        private void ScheduleRosterRefresh(bool forcePlayerReload = false)
        {
            var source = GetCurrentSnapshot();
            if (source.GameflowPhase == GameflowPhase.GameStart)
            {
                return;
            }

            var sourceSignature = BuildRosterSourceSignature(source);
            CancellationToken lifetimeToken;
            CancellationToken phaseToken;

            lock (_stateSync)
            {
                if (!_started || _lifetimeCts is null || _phaseCts is null)
                {
                    return;
                }

                lifetimeToken = _lifetimeCts.Token;
                phaseToken = _phaseCts.Token;
            }

            CancellationTokenSource previousCts;
            CancellationTokenSource rosterCts;
            long generation;
            lock (_rosterSync)
            {
                if (!forcePlayerReload &&
                    string.Equals(_rosterSourceSignature, sourceSignature,
                        StringComparison.Ordinal))
                {
                    return;
                }

                previousCts = _rosterCts;
                _rosterSourceSignature = sourceSignature;
                generation = ++_rosterGeneration;
                rosterCts = CancellationTokenSource.CreateLinkedTokenSource(
                    lifetimeToken, phaseToken);
                _rosterCts = rosterCts;
                _rosterTask = Task.CompletedTask;
            }

            previousCts?.Cancel();
            previousCts?.Dispose();

            var task = ResolveAndEnrichRosterAsync(source, sourceSignature,
                generation, forcePlayerReload, rosterCts.Token);
            lock (_rosterSync)
            {
                if (generation == _rosterGeneration &&
                    ReferenceEquals(_rosterCts, rosterCts))
                {
                    _rosterTask = task;
                }
            }
        }

        private async Task ResolveAndEnrichRosterAsync(LiveMatchSnapshot source,
            string sourceSignature, long generation, bool forcePlayerReload,
            CancellationToken cancellationToken)
        {
            try
            {
                if (!IsRosterPhase(source.GameflowPhase))
                {
                    PublishRoster(generation, cancellationToken, null);
                    return;
                }

                RosterDefinition definition;
                if (source.GameflowPhase == GameflowPhase.ChampSelect)
                {
                    definition = BuildChampionSelectRoster(source, sourceSignature);
                }
                else
                {
                    PublishRoster(generation, cancellationToken,
                        CreateTransitionRoster(source, sourceSignature, true));
                    definition = await BuildGameflowRosterAsync(source, sourceSignature,
                        cancellationToken).ConfigureAwait(false);
                }

                cancellationToken.ThrowIfCancellationRequested();
                if (definition is null)
                {
                    PublishRoster(generation, cancellationToken,
                        CreateTransitionRoster(source, sourceSignature, false));
                    MarkRosterRetryable(generation);
                    return;
                }

                if (!forcePlayerReload)
                {
                    definition = CarryForwardPlayerData(definition, source.Roster);
                }

                var roster = new LiveMatchRosterSnapshot
                {
                    GameId = definition.GameId,
                    SourcePhase = source.GameflowPhase,
                    Signature = sourceSignature,
                    IsResolving = false,
                    MyTeam = definition.MyTeam,
                    TheirTeam = definition.TheirTeam
                };
                PublishRoster(generation, cancellationToken, roster);

                var tasks = definition.MyTeam
                    .Select((player, index) => (Player: player, Index: index, IsMyTeam: true))
                    .Concat(definition.TheirTeam.Select((player, index) =>
                        (Player: player, Index: index, IsMyTeam: false)))
                    .Where(item => NeedsPlayerLoad(item.Player))
                    .Select(item => EnrichPlayerAsync(definition.GameId, item.Player,
                        item.Index, item.IsMyTeam, generation, cancellationToken))
                    .ToArray();

                await Task.WhenAll(tasks).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
            }
            catch (Exception exception)
            {
                PublishRosterFailure(generation, cancellationToken,
                    "Unable to assemble the live-match roster.", exception);
                MarkRosterRetryable(generation);
            }
        }

        private async Task<RosterDefinition> BuildGameflowRosterAsync(
            LiveMatchSnapshot source, string sourceSignature,
            CancellationToken cancellationToken)
        {
            var gameData = source.GameflowSession?.GameData;
            if (!HasGameflowTeams(gameData))
            {
                return null;
            }

            var currentSummoner = _currentSummoner;
            if (!CanLocateCurrentSummoner(gameData, currentSummoner))
            {
                currentSummoner = GetChampionSelectLocalSummoner(source.ChampionSelect);
            }

            if (!CanLocateCurrentSummoner(gameData, currentSummoner))
            {
                currentSummoner = await _summonerService.GetCurrentSummoner(cancellationToken)
                    .ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
            }

            if (!CanLocateCurrentSummoner(gameData, currentSummoner))
            {
                return null;
            }

            var currentInTeamOne = ContainsAccount(gameData.TeamOne, currentSummoner);
            var currentInTeamTwo = ContainsAccount(gameData.TeamTwo, currentSummoner);
            if (currentInTeamOne == currentInTeamTwo)
            {
                return null;
            }

            _currentSummoner = currentSummoner;
            var mySource = currentInTeamOne ? gameData.TeamOne : gameData.TeamTwo;
            var theirSource = currentInTeamOne ? gameData.TeamTwo : gameData.TeamOne;
            var myTeam = NormalizeRoster(mySource.Select((member, index) =>
                FromGameflowMember(member, FindPlayerSelection(gameData, member), index,
                    currentSummoner)), false);
            var theirTeam = NormalizeRoster(theirSource.Select((member, index) =>
                FromGameflowMember(member, FindPlayerSelection(gameData, member), index,
                    currentSummoner)), false);
            return new RosterDefinition(gameData.GameId, sourceSignature, myTeam, theirTeam);
        }

        private static RosterDefinition BuildChampionSelectRoster(
            LiveMatchSnapshot source, string sourceSignature)
        {
            var championSelect = source.ChampionSelect;
            var myTeam = NormalizeRoster((championSelect?.MyTeam ?? [])
                .Select((member, index) => FromChampionSelectMember(member, index,
                    championSelect?.LocalPlayerCellId ?? long.MinValue, false)), false);
            var theirTeam = NormalizeRoster((championSelect?.TheirTeam ?? [])
                .Select((member, index) => FromChampionSelectMember(member, index,
                    championSelect?.LocalPlayerCellId ?? long.MinValue, true)), true);
            var gameId = (championSelect?.GameId ?? 0) > 0
                ? championSelect.GameId
                : GetGameId(source);
            return new RosterDefinition(gameId, sourceSignature, myTeam, theirTeam);
        }

        private static RosterDefinition CarryForwardPlayerData(
            RosterDefinition definition, LiveMatchRosterSnapshot previousRoster)
        {
            if (definition is null || previousRoster is null)
            {
                return definition;
            }

            var previousPlayers = (previousRoster.MyTeam ??
                    Array.Empty<LiveMatchPlayerSnapshot>())
                .Concat(previousRoster.TheirTeam ??
                    Array.Empty<LiveMatchPlayerSnapshot>())
                .Where(player => player is not null &&
                    !string.IsNullOrWhiteSpace(player.Puuid) &&
                    player.DataState is LiveMatchPlayerDataState.Loaded or
                        LiveMatchPlayerDataState.Error)
                .GroupBy(player => NormalizePuuid(player.Puuid),
                    StringComparer.Ordinal)
                .ToDictionary(group => group.Key,
                    group => group.OrderByDescending(player =>
                            player.DataState == LiveMatchPlayerDataState.Loaded)
                        .First(),
                    StringComparer.Ordinal);

            if (previousPlayers.Count == 0)
            {
                return definition;
            }

            return new RosterDefinition(definition.GameId, definition.Signature,
                definition.MyTeam.Select(player => CarryForwardPlayerData(
                    player, previousPlayers)).ToArray(),
                definition.TheirTeam.Select(player => CarryForwardPlayerData(
                    player, previousPlayers)).ToArray());
        }

        private static LiveMatchPlayerSnapshot CarryForwardPlayerData(
            LiveMatchPlayerSnapshot player,
            IReadOnlyDictionary<string, LiveMatchPlayerSnapshot> previousPlayers)
        {
            if (player is null || string.IsNullOrWhiteSpace(player.Puuid) ||
                !previousPlayers.TryGetValue(NormalizePuuid(player.Puuid),
                    out var previous))
            {
                return player;
            }

            var next = ClonePlayer(player);
            if (next.ChampionId == previous.ChampionId)
            {
                next.ChampionIcon = previous.ChampionIcon;
            }
            if (next.Spell1Id == previous.Spell1Id)
            {
                next.Spell1Icon = previous.Spell1Icon;
            }
            if (next.Spell2Id == previous.Spell2Id)
            {
                next.Spell2Icon = previous.Spell2Icon;
            }

            next.DisplayName = FirstNotEmpty(next.DisplayName, previous.DisplayName);
            if (previous.DataState == LiveMatchPlayerDataState.Error)
            {
                next.DataState = LiveMatchPlayerDataState.Error;
                next.Error = previous.Error;
                return next;
            }

            next.Summoner = previous.Summoner;
            next.SoloRank = previous.SoloRank;
            next.RecentWins = previous.RecentWins;
            next.RecentLosses = previous.RecentLosses;
            next.RecentMatchCount = previous.RecentMatchCount;
            next.AverageKda = previous.AverageKda;
            next.RecentResults = previous.RecentResults?.ToArray() ?? Array.Empty<bool>();
            next.RecentMatches = (previous.RecentMatches ??
                    Array.Empty<LiveMatchRecentMatchSnapshot>())
                .Select(CloneRecentMatch)
                .ToArray();
            next.DataState = LiveMatchPlayerDataState.Loaded;
            next.Error = string.Empty;
            return next;
        }

        private async Task EnrichPlayerAsync(long gameId, LiveMatchPlayerSnapshot player,
            int index, bool isMyTeam, long generation,
            CancellationToken cancellationToken)
        {
            var visualsTask = LoadAndPublishPlayerVisualsAsync(player, index, isMyTeam,
                generation, cancellationToken);
            if (player.DataState != LiveMatchPlayerDataState.Loading)
            {
                await visualsTask.WaitAsync(cancellationToken).ConfigureAwait(false);
                return;
            }

            var gateEntered = false;
            try
            {
                await _playerLoadGate.WaitAsync(cancellationToken).ConfigureAwait(false);
                gateEntered = true;

                var performance = await GetPlayerPerformanceAsync(player.Puuid,
                    cancellationToken).ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
                PublishPlayerUpdate(generation, cancellationToken, isMyTeam, index,
                    current => ApplyPerformance(current, performance));
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                Log.Error(exception,
                    "Unable to enrich live-match player data for game {GameId}, cell {CellId}",
                    gameId, player.CellId);
                PublishPlayerUpdate(generation, cancellationToken, isMyTeam, index,
                    current =>
                    {
                        current.DataState = LiveMatchPlayerDataState.Error;
                        current.Error = "Unable to load player performance.";
                        return current;
                    });
            }
            finally
            {
                if (gateEntered)
                {
                    _playerLoadGate.Release();
                }
            }

            await visualsTask.WaitAsync(cancellationToken).ConfigureAwait(false);
        }

        private async Task LoadAndPublishPlayerVisualsAsync(
            LiveMatchPlayerSnapshot player, int index, bool isMyTeam,
            long generation, CancellationToken cancellationToken)
        {
            var visuals = await LoadPlayerVisualsAsync(player).ConfigureAwait(false);
            PublishPlayerUpdate(generation, cancellationToken, isMyTeam, index,
                current =>
                {
                    ApplyVisuals(current, visuals);
                    return current;
                });
        }

        private async Task<PlayerVisuals> LoadPlayerVisualsAsync(
            LiveMatchPlayerSnapshot player)
        {
            try
            {
                var championTask = player.ChampionId > 0
                    ? _gameResourceManager.GetChampoinIconByIdAsync(player.ChampionId)
                    : Task.FromResult<string>(null);
                var spell1Task = player.Spell1Id > 0
                    ? _gameResourceManager.GetSpellIconByIdAsync(player.Spell1Id)
                    : Task.FromResult<string>(null);
                var spell2Task = player.Spell2Id > 0
                    ? _gameResourceManager.GetSpellIconByIdAsync(player.Spell2Id)
                    : Task.FromResult<string>(null);
                await Task.WhenAll(championTask, spell1Task, spell2Task)
                    .ConfigureAwait(false);
                return new PlayerVisuals(await championTask.ConfigureAwait(false),
                    await spell1Task.ConfigureAwait(false),
                    await spell2Task.ConfigureAwait(false));
            }
            catch (Exception exception)
            {
                Log.Warning(exception,
                    "Unable to load live-match icons for cell {CellId}", player.CellId);
                return default;
            }
        }

        private Task<PlayerPerformanceData> GetPlayerPerformanceAsync(string puuid,
            CancellationToken cancellationToken)
        {
            var key = NormalizePuuid(puuid);
            CancellationToken playerLoadToken;
            lock (_stateSync)
            {
                playerLoadToken = _playerLoadCts?.Token ?? new CancellationToken(true);
            }

            var lazy = _playerCache.GetOrAdd(key, _ =>
                new Lazy<Task<PlayerPerformanceData>>(
                    () => LoadPlayerPerformanceAsync(puuid, playerLoadToken),
                    LazyThreadSafetyMode.ExecutionAndPublication));
            return AwaitCachedPlayerPerformanceAsync(key, lazy)
                .WaitAsync(cancellationToken);
        }

        private async Task<PlayerPerformanceData> AwaitCachedPlayerPerformanceAsync(
            string key, Lazy<Task<PlayerPerformanceData>> lazy)
        {
            try
            {
                return await lazy.Value.ConfigureAwait(false);
            }
            catch
            {
                if (_playerCache.TryGetValue(key, out var current) &&
                    ReferenceEquals(current, lazy))
                {
                    _playerCache.TryRemove(key, out _);
                }

                throw;
            }
        }

        private async Task<PlayerPerformanceData> LoadPlayerPerformanceAsync(string puuid,
            CancellationToken cancellationToken)
        {
            var summonerTask = _summonerService.SearchSummonerByPuuid(
                puuid, cancellationToken);
            var rankTask = _summonerService.GetRankStatsByPuuid(puuid, cancellationToken);
            var matchesTask = _summonerService.GetMatchHistoryAsync(
                puuid, cancellationToken);

            await Task.WhenAll(summonerTask, rankTask, matchesTask).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();

            var summoner = await summonerTask.ConfigureAwait(false);
            if (summoner is null)
            {
                throw new InvalidOperationException("The summoner account is unavailable.");
            }

            var rankJson = await rankTask.ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(rankJson))
            {
                throw new InvalidOperationException("Ranked stats are unavailable.");
            }
            var rank = ParseSoloRank(rankJson);
            var matchResult = await matchesTask.ConfigureAwait(false);
            if (matchResult is null || !matchResult.Succeeded)
            {
                throw new InvalidOperationException(matchResult?.Error ??
                    "Recent match history is unavailable.");
            }

            var matches = (matchResult.Matches ?? Array.Empty<Match>())
                .Take(RecentMatchCount)
                .ToArray();
            var recentMatches = await BuildRecentMatchesAsync(matches, cancellationToken)
                .ConfigureAwait(false);
            var wins = recentMatches.Count(value => value.IsWin);
            var losses = recentMatches.Count - wins;
            var killsAndAssists = recentMatches.Sum(value => value.Kills + value.Assists);
            var deaths = recentMatches.Sum(value => value.Deaths);
            var averageKda = recentMatches.Count == 0
                ? 0d
                : killsAndAssists / (double)Math.Max(1, deaths);

            return new PlayerPerformanceData(summoner, rank, wins, losses,
                averageKda, recentMatches.Count,
                recentMatches.Select(value => value.IsWin).ToArray(),
                recentMatches);
        }

        private async Task<IReadOnlyList<LiveMatchRecentMatchSnapshot>>
            BuildRecentMatchesAsync(IReadOnlyList<Match> matches,
                CancellationToken cancellationToken)
        {
            var recentMatches = (matches ?? Array.Empty<Match>())
                .Select(match =>
                {
                    var participant = match?.Participants?.FirstOrDefault();
                    var stats = participant?.Stats;
                    if (stats is null)
                    {
                        return null;
                    }

                    return new LiveMatchRecentMatchSnapshot
                    {
                        GameId = match.GameId,
                        GameCreation = match.GameCreation,
                        QueueId = match.QueueId,
                        GameMode = FirstNotEmpty(match.DisplayGameMode, match.GameMode),
                        ChampionId = participant.ChampionId,
                        ChampionIcon = participant.ChampionIcon ?? string.Empty,
                        IsWin = stats.Win,
                        Kills = stats.Kills,
                        Deaths = stats.Deaths,
                        Assists = stats.Assists
                    };
                })
                .Where(match => match is not null)
                .ToArray();

            var missingChampionIds = recentMatches
                .Where(match => match.ChampionId > 0 &&
                    string.IsNullOrWhiteSpace(match.ChampionIcon))
                .Select(match => match.ChampionId)
                .Distinct()
                .ToArray();
            var championIcons = new Dictionary<int, string>();
            foreach (var championId in missingChampionIds)
            {
                cancellationToken.ThrowIfCancellationRequested();
                championIcons[championId] = await GetRecentChampionIconAsync(championId)
                    .ConfigureAwait(false);
            }

            foreach (var match in recentMatches)
            {
                if (string.IsNullOrWhiteSpace(match.ChampionIcon) &&
                    championIcons.TryGetValue(match.ChampionId, out var championIcon))
                {
                    match.ChampionIcon = championIcon ?? string.Empty;
                }
            }

            return recentMatches;
        }

        private async Task<string> GetRecentChampionIconAsync(int championId)
        {
            var lazy = _recentChampionIconCache.GetOrAdd(championId, id =>
                new Lazy<Task<string>>(
                    () => _gameResourceManager.GetChampoinIconByIdAsync(id),
                    LazyThreadSafetyMode.ExecutionAndPublication));
            try
            {
                return await lazy.Value.ConfigureAwait(false) ?? string.Empty;
            }
            catch (Exception exception)
            {
                if (_recentChampionIconCache.TryGetValue(championId, out var current) &&
                    ReferenceEquals(current, lazy))
                {
                    _recentChampionIconCache.TryRemove(championId, out _);
                }

                Log.Debug(exception,
                    "Unable to load recent-match champion icon {ChampionId}", championId);
                return string.Empty;
            }
        }

        private void PublishRoster(long generation, CancellationToken cancellationToken,
            LiveMatchRosterSnapshot roster)
        {
            PublishSnapshot(snapshot =>
            {
                if (!IsCurrentRosterGeneration(generation, cancellationToken))
                {
                    return snapshot;
                }

                var next = CopySnapshot(snapshot);
                next.Roster = CloneRoster(roster);
                ApplyRosterDataQuality(next);
                return next;
            });
        }

        private void PublishRosterFailure(long generation,
            CancellationToken cancellationToken, string error, Exception exception)
        {
            PublishSnapshot(snapshot =>
            {
                if (!IsCurrentRosterGeneration(generation, cancellationToken))
                {
                    return snapshot;
                }

                var next = CopySnapshot(snapshot);
                if (next.Roster is not null)
                {
                    next.Roster.IsResolving = false;
                }
                next.Error = error;
                next.Errors = (next.Errors ?? Array.Empty<string>())
                    .Concat([error])
                    .Distinct(StringComparer.Ordinal)
                    .ToArray();
                next.DataQuality = DataQuality.Partial;
                return next;
            }, new SnapshotErrorLogContext(
                "Unable to assemble the live-match roster.", [exception]));
        }

        private void PublishPlayerUpdate(long generation,
            CancellationToken cancellationToken, bool isMyTeam, int index,
            Func<LiveMatchPlayerSnapshot, LiveMatchPlayerSnapshot> update)
        {
            PublishSnapshot(snapshot =>
            {
                if (!IsCurrentRosterGeneration(generation, cancellationToken) ||
                    snapshot.Roster is null)
                {
                    return snapshot;
                }

                var next = CopySnapshot(snapshot);
                var roster = next.Roster;
                var team = (isMyTeam ? roster.MyTeam : roster.TheirTeam).ToArray();
                if (index < 0 || index >= team.Length)
                {
                    return snapshot;
                }

                team[index] = update(ClonePlayer(team[index])) ?? team[index];
                if (isMyTeam)
                {
                    roster.MyTeam = team;
                }
                else
                {
                    roster.TheirTeam = team;
                }
                ApplyRosterDataQuality(next);
                return next;
            });
        }

        private bool IsCurrentRosterGeneration(long generation,
            CancellationToken cancellationToken)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return false;
            }

            lock (_rosterSync)
            {
                return generation == _rosterGeneration &&
                    _rosterCts is not null && !_rosterCts.IsCancellationRequested;
            }
        }

        private void MarkRosterRetryable(long generation)
        {
            lock (_rosterSync)
            {
                if (generation == _rosterGeneration)
                {
                    _rosterSourceSignature = string.Empty;
                }
            }
        }

        private Task CancelRosterEnrichment(bool clearCache, bool resetSignature)
        {
            CancellationTokenSource rosterCts;
            Task rosterTask;
            lock (_rosterSync)
            {
                rosterCts = _rosterCts;
                rosterTask = _rosterTask;
                _rosterCts = null;
                _rosterTask = Task.CompletedTask;
                _rosterGeneration++;
                if (resetSignature)
                {
                    _rosterSourceSignature = string.Empty;
                }
            }

            rosterCts?.Cancel();
            rosterCts?.Dispose();
            if (clearCache)
            {
                _playerCache.Clear();
            }
            return rosterTask ?? Task.CompletedTask;
        }

        private void ResetPlayerLoadLifetime()
        {
            CancellationTokenSource previousCts;
            lock (_stateSync)
            {
                previousCts = _playerLoadCts;
                _playerLoadCts = _started && _lifetimeCts is not null
                    ? CancellationTokenSource.CreateLinkedTokenSource(_lifetimeCts.Token)
                    : null;
            }

            previousCts?.Cancel();
            previousCts?.Dispose();
            _playerCache.Clear();
        }

        private static LiveMatchPlayerSnapshot ApplyPerformance(
            LiveMatchPlayerSnapshot player, PlayerPerformanceData performance)
        {
            player.Summoner = performance.Summoner;
            player.SoloRank = performance.SoloRank;
            player.RecentWins = performance.Wins;
            player.RecentLosses = performance.Losses;
            player.RecentMatchCount = performance.MatchCount;
            player.AverageKda = performance.AverageKda;
            player.RecentResults = performance.RecentResults.ToArray();
            player.RecentMatches = performance.RecentMatches
                .Select(CloneRecentMatch)
                .ToArray();
            player.DisplayName = FormatSummonerName(performance.Summoner,
                player.DisplayName);
            player.DataState = LiveMatchPlayerDataState.Loaded;
            player.Error = string.Empty;
            return player;
        }

        private static void ApplyVisuals(LiveMatchPlayerSnapshot player,
            PlayerVisuals visuals)
        {
            player.ChampionIcon = visuals.ChampionIcon ?? string.Empty;
            player.Spell1Icon = visuals.Spell1Icon ?? string.Empty;
            player.Spell2Icon = visuals.Spell2Icon ?? string.Empty;
        }

        private static bool NeedsPlayerLoad(LiveMatchPlayerSnapshot player)
        {
            return player is not null &&
                (player.ChampionId > 0 || player.Spell1Id > 0 || player.Spell2Id > 0 ||
                 player.DataState == LiveMatchPlayerDataState.Loading);
        }

        private static void ApplyRosterDataQuality(LiveMatchSnapshot snapshot)
        {
            if (snapshot.ConnectionState != ConnectionState.Connected ||
                snapshot.DataQuality is DataQuality.Error or DataQuality.Stale)
            {
                return;
            }

            var roster = snapshot.Roster;
            if (roster is null)
            {
                return;
            }

            var players = roster.MyTeam.Concat(roster.TheirTeam).ToArray();
            var incomplete = roster.IsResolving || players.Length != TeamSize * 2 ||
                players.Any(player => player.DataState != LiveMatchPlayerDataState.Loaded);
            snapshot.DataQuality = incomplete || (snapshot.Errors?.Count ?? 0) > 0
                ? DataQuality.Partial
                : DataQuality.Complete;
        }

        private static LiveMatchPlayerSnapshot FromChampionSelectMember(
            ChampionSelectTeamMemberSnapshot member, int index,
            long localPlayerCellId, bool isEnemy)
        {
            member ??= new ChampionSelectTeamMemberSnapshot();
            var explicitlyHidden = string.Equals(member.NameVisibilityType, "HIDDEN",
                StringComparison.OrdinalIgnoreCase);
            var hidden = isEnemy || explicitlyHidden;
            var puuid = hidden ? string.Empty : member.Puuid ?? string.Empty;
            return new LiveMatchPlayerSnapshot
            {
                Slot = index,
                CellId = member.CellId,
                ChampionId = member.ChampionId > 0
                    ? member.ChampionId
                    : member.ChampionPickIntent,
                Spell1Id = member.Spell1Id,
                Spell2Id = member.Spell2Id,
                Puuid = puuid,
                Position = member.AssignedPosition ?? string.Empty,
                DisplayName = hidden
                    ? string.Empty
                    : FormatRiotId(member.GameName, member.TagLine),
                IsLocalPlayer = member.CellId == localPlayerCellId,
                IsHidden = hidden,
                DataState = hidden
                    ? LiveMatchPlayerDataState.Hidden
                    : string.IsNullOrWhiteSpace(puuid)
                        ? LiveMatchPlayerDataState.Unavailable
                        : LiveMatchPlayerDataState.Loading
            };
        }

        private static LiveMatchPlayerSnapshot FromGameflowMember(
            GameflowTeamMember member, GameflowPlayerSelection selection, int index,
            SummonerAccount currentSummoner)
        {
            member ??= new GameflowTeamMember();
            var puuid = member.Puuid ?? string.Empty;
            var displayName = member.SummonerName ?? string.Empty;
            var hidden = string.IsNullOrWhiteSpace(puuid) &&
                string.IsNullOrWhiteSpace(displayName);
            return new LiveMatchPlayerSnapshot
            {
                Slot = index,
                CellId = member.CellId != 0 ? member.CellId : member.TeamParticipantId,
                ChampionId = member.ChampionId > 0
                    ? member.ChampionId
                    : selection?.ChampionId ?? 0,
                Spell1Id = member.Spell1Id > 0
                    ? member.Spell1Id
                    : selection?.Spell1Id ?? 0,
                Spell2Id = member.Spell2Id > 0
                    ? member.Spell2Id
                    : selection?.Spell2Id ?? 0,
                Puuid = puuid,
                Position = FirstNotEmpty(member.SelectedPosition, member.AssignedPosition),
                DisplayName = displayName,
                IsLocalPlayer = IsSameAccount(member, currentSummoner),
                IsHidden = hidden,
                DataState = hidden
                    ? LiveMatchPlayerDataState.Hidden
                    : string.IsNullOrWhiteSpace(puuid)
                        ? LiveMatchPlayerDataState.Unavailable
                        : LiveMatchPlayerDataState.Loading
            };
        }

        private static IReadOnlyList<LiveMatchPlayerSnapshot> NormalizeRoster(
            IEnumerable<LiveMatchPlayerSnapshot> members, bool hiddenPlaceholders)
        {
            var values = (members ?? [])
                .OrderBy(member => PositionOrder(member.Position))
                .ThenBy(member => member.Slot)
                .Take(TeamSize)
                .ToList();
            while (values.Count < TeamSize)
            {
                values.Add(new LiveMatchPlayerSnapshot
                {
                    Slot = values.Count,
                    CellId = long.MinValue + values.Count,
                    IsHidden = hiddenPlaceholders,
                    IsPlaceholder = true,
                    DataState = hiddenPlaceholders
                        ? LiveMatchPlayerDataState.Hidden
                        : LiveMatchPlayerDataState.Placeholder
                });
            }
            return values;
        }

        private static LiveMatchRosterSnapshot CreateTransitionRoster(
            LiveMatchSnapshot source, string sourceSignature, bool isResolving)
        {
            var gameId = GetGameId(source);
            var previous = source?.Roster;
            var canRetainPrevious = previous is not null &&
                (gameId <= 0 || previous.GameId <= 0 || previous.GameId == gameId);
            return new LiveMatchRosterSnapshot
            {
                GameId = gameId,
                SourcePhase = source?.GameflowPhase ?? GameflowPhase.Unknown,
                Signature = sourceSignature,
                IsResolving = isResolving,
                MyTeam = canRetainPrevious
                    ? (previous.MyTeam ?? Array.Empty<LiveMatchPlayerSnapshot>())
                        .Select(ClonePlayer).ToArray()
                    : Array.Empty<LiveMatchPlayerSnapshot>(),
                TheirTeam = canRetainPrevious
                    ? (previous.TheirTeam ?? Array.Empty<LiveMatchPlayerSnapshot>())
                        .Select(ClonePlayer).ToArray()
                    : Array.Empty<LiveMatchPlayerSnapshot>()
            };
        }

        private static string BuildRosterSourceSignature(LiveMatchSnapshot snapshot)
        {
            var builder = new StringBuilder()
                .Append(snapshot.GameflowPhase)
                .Append(':')
                .Append(GetGameId(snapshot));
            var isGameflowPhase = snapshot.GameflowPhase is GameflowPhase.InProgress or
                GameflowPhase.Reconnect;
            if (snapshot.GameflowPhase == GameflowPhase.ChampSelect)
            {
                var championSelect = snapshot.ChampionSelect;
                builder.Append(':').Append(championSelect?.LocalPlayerCellId ?? 0);
                AppendChampionSelectSignature(builder, championSelect?.MyTeam, false);
                AppendChampionSelectSignature(builder, championSelect?.TheirTeam, true);
            }
            else if (isGameflowPhase)
            {
                AppendGameflowSignature(builder,
                    snapshot.GameflowSession?.GameData?.TeamOne);
                AppendGameflowSignature(builder,
                    snapshot.GameflowSession?.GameData?.TeamTwo);
                AppendGameflowSelectionSignature(builder,
                    snapshot.GameflowSession?.GameData?.PlayerChampionSelections);
            }
            return builder.ToString();
        }

        private static void AppendChampionSelectSignature(StringBuilder builder,
            IEnumerable<ChampionSelectTeamMemberSnapshot> team, bool hideIdentity)
        {
            builder.Append('|');
            foreach (var member in team ?? [])
            {
                builder.Append(member?.CellId ?? 0).Append(',')
                    .Append(member?.ChampionId ?? 0).Append(',')
                    .Append(member?.ChampionPickIntent ?? 0).Append(',')
                    .Append(member?.Spell1Id ?? 0).Append(',')
                    .Append(member?.Spell2Id ?? 0).Append(',')
                    .Append(member?.AssignedPosition).Append(',')
                    .Append(member?.NameVisibilityType).Append(',');
                if (!hideIdentity)
                {
                    builder.Append(member?.Puuid).Append(',')
                        .Append(member?.GameName).Append(',')
                        .Append(member?.TagLine);
                }
                builder.Append(';');
            }
        }

        private static void AppendGameflowSignature(StringBuilder builder,
            IEnumerable<GameflowTeamMember> team)
        {
            builder.Append('|');
            foreach (var member in team ?? [])
            {
                builder.Append(member?.CellId ?? 0).Append(',')
                    .Append(member?.TeamParticipantId ?? 0).Append(',')
                    .Append(member?.ChampionId ?? 0).Append(',')
                    .Append(member?.Spell1Id ?? 0).Append(',')
                    .Append(member?.Spell2Id ?? 0).Append(',')
                    .Append(member?.Puuid).Append(',')
                    .Append(member?.SummonerId ?? 0).Append(',')
                    .Append(member?.SummonerName).Append(',')
                    .Append(member?.SelectedPosition).Append(',')
                    .Append(member?.AssignedPosition).Append(';');
            }
        }

        private static void AppendGameflowSelectionSignature(StringBuilder builder,
            IEnumerable<GameflowPlayerSelection> selections)
        {
            builder.Append('|');
            foreach (var selection in selections ?? [])
            {
                builder.Append(selection?.Puuid).Append(',')
                    .Append(selection?.ChampionId ?? 0).Append(',')
                    .Append(selection?.Spell1Id ?? 0).Append(',')
                    .Append(selection?.Spell2Id ?? 0).Append(';');
            }
        }

        private static bool HasGameflowTeams(GameflowGameData gameData)
        {
            return gameData is not null && (gameData.TeamOne?.Count ?? 0) > 0 &&
                (gameData.TeamTwo?.Count ?? 0) > 0;
        }

        private static bool CanLocateCurrentSummoner(GameflowGameData gameData,
            SummonerAccount summoner)
        {
            return gameData is not null && summoner is not null &&
                (ContainsAccount(gameData.TeamOne, summoner) ||
                 ContainsAccount(gameData.TeamTwo, summoner));
        }

        private static SummonerAccount GetChampionSelectLocalSummoner(
            ChampionSelectSnapshot championSelect)
        {
            var member = championSelect?.MyTeam?.FirstOrDefault(value =>
                value is not null && value.CellId == championSelect.LocalPlayerCellId);
            if (member is null ||
                (string.IsNullOrWhiteSpace(member.Puuid) && member.SummonerId <= 0))
            {
                return null;
            }

            return new SummonerAccount
            {
                Puuid = member.Puuid,
                SummonerId = member.SummonerId,
                GameName = member.GameName,
                TagLine = member.TagLine
            };
        }

        private static GameflowPlayerSelection FindPlayerSelection(
            GameflowGameData gameData, GameflowTeamMember member)
        {
            var puuid = NormalizePuuid(member?.Puuid);
            if (string.IsNullOrWhiteSpace(puuid))
            {
                return null;
            }

            return (gameData?.PlayerChampionSelections ?? []).FirstOrDefault(selection =>
                string.Equals(NormalizePuuid(selection?.Puuid), puuid,
                    StringComparison.Ordinal));
        }

        private static bool ContainsAccount(IEnumerable<GameflowTeamMember> team,
            SummonerAccount summoner)
        {
            return (team ?? []).Any(member => IsSameAccount(member, summoner));
        }

        private static bool IsSameAccount(GameflowTeamMember member,
            SummonerAccount summoner)
        {
            if (member is null || summoner is null)
            {
                return false;
            }

            return !string.IsNullOrWhiteSpace(member.Puuid) &&
                    !string.IsNullOrWhiteSpace(summoner.Puuid) &&
                    string.Equals(member.Puuid, summoner.Puuid,
                        StringComparison.OrdinalIgnoreCase) ||
                member.SummonerId > 0 && summoner.SummonerId > 0 &&
                    member.SummonerId == summoner.SummonerId;
        }

        private static Rank ParseSoloRank(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                return null;
            }

            return JObject.Parse(json)["queueMap"]?["RANKED_SOLO_5x5"]?.ToObject<Rank>();
        }

        private static int PositionOrder(string position)
        {
            return NormalizePosition(position) switch
            {
                "TOP" => 0,
                "JUNGLE" => 1,
                "MIDDLE" => 2,
                "BOTTOM" => 3,
                "UTILITY" => 4,
                _ => 5
            };
        }

        private static string NormalizePosition(string position)
        {
            return position?.Trim().ToUpperInvariant() switch
            {
                "JUNGLE" or "JUG" => "JUNGLE",
                "MIDDLE" or "MID" => "MIDDLE",
                "BOTTOM" or "BOT" => "BOTTOM",
                "UTILITY" or "SUPPORT" or "SUP" => "UTILITY",
                "TOP" => "TOP",
                _ => string.Empty
            };
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

        private void TriggerAutomation(PhaseContext context)
        {
            if (!IsCurrentPhase(context))
            {
                return;
            }

            if (context.Phase == GameflowPhase.ReadyCheck &&
                AutomationSettings.AutoAcceptReadyCheck)
            {
                StartAutoAccept(context);
            }

            if (context.Phase == GameflowPhase.Reconnect && AutomationSettings.AutoReconnect)
            {
                if (CanAutoReconnect(context))
                {
                    StartAutoReconnect(context);
                }
                else
                {
                    CancelAutoReconnect();
                }
            }
            else
            {
                CancelAutoReconnect();
            }

            if (context.Phase == GameflowPhase.ChampSelect &&
                AutomationSettings.AutoSwapAramBench)
            {
                StartAutoAramBenchSwap(context);
            }
            else
            {
                CancelAutoAramBenchSwap(resetState: true);
            }

            if (context.Phase == GameflowPhase.ChampSelect &&
                (AutomationSettings.AutoPickChampion ||
                 AutomationSettings.AutoBanChampion))
            {
                StartAutoChampionSelectAction(context);
            }
            else
            {
                CancelAutoChampionSelectAction(resetState: true);
            }
        }

        private void StartAutoAramBenchSwap(PhaseContext context)
        {
            var snapshot = GetCurrentSnapshot();
            var preferredChampionIds = AutomationSettings.PreferredAramChampionIds?
                .Where(championId => championId > 0)
                .Distinct()
                .ToArray() ?? [];
            var stateSignature = BuildAramBenchSwapState(
                context, snapshot, preferredChampionIds);
            var targetChampionId = FindPreferredAramBenchChampion(
                snapshot, preferredChampionIds);

            CancellationTokenSource previousCts = null;
            lock (_stateSync)
            {
                if (!_started || context.Version != _phaseVersion ||
                    context.Token.IsCancellationRequested ||
                    !AutomationSettings.AutoSwapAramBench ||
                    string.Equals(_lastAramBenchSwapState, stateSignature,
                        StringComparison.Ordinal) ||
                    (string.Equals(_aramBenchSwapFailedState, stateSignature,
                        StringComparison.Ordinal) && _aramBenchSwapFailureRetryConsumed))
                {
                    return;
                }

                previousCts = _aramBenchSwapAutomationCts;
                _aramBenchSwapAutomationCts = null;
                _lastAramBenchSwapState = stateSignature;

                if (targetChampionId > 0)
                {
                    _aramBenchSwapAutomationCts =
                        CancellationTokenSource.CreateLinkedTokenSource(context.Token);
                    var token = _aramBenchSwapAutomationCts.Token;
                    _aramBenchSwapAutomationTask = RunAramBenchSwapAsync(
                        context,
                        stateSignature,
                        targetChampionId,
                        token);
                }
            }

            previousCts?.Cancel();
            previousCts?.Dispose();
        }

        private async Task RunAramBenchSwapAsync(
            PhaseContext context,
            string stateSignature,
            int championId,
            CancellationToken cancellationToken)
        {
            var operationId = Guid.NewGuid();
            var stopwatch = Stopwatch.StartNew();
            var attemptCount = 0;
            Exception lastError = null;

            try
            {
                for (var attempt = 0; attempt < AramBenchSwapRetryDelays.Length; attempt++)
                {
                    var delay = AramBenchSwapRetryDelays[attempt];
                    if (delay > TimeSpan.Zero)
                    {
                        await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
                    }

                    if (!IsCurrentAramBenchSwapState(
                            context, stateSignature, championId))
                    {
                        LogAramBenchSwapResult(
                            LogEventLevel.Information,
                            "Cancelled",
                            operationId,
                            context,
                            championId,
                            attemptCount,
                            stopwatch.ElapsedMilliseconds,
                            "Automatic ARAM champion swap was cancelled because the bench changed.");
                        return;
                    }

                    if (!_leagueClient.Connected || !_httpService.IsInitialized)
                    {
                        LogAramBenchSwapResult(
                            LogEventLevel.Warning,
                            "Rejected",
                            operationId,
                            context,
                            championId,
                            attemptCount,
                            stopwatch.ElapsedMilliseconds,
                            "Automatic ARAM champion swap was rejected because LCU is unavailable.");
                        return;
                    }

                    attemptCount++;
                    try
                    {
                        await _gameService.SwapAramBenchChampionAsync(
                            championId, cancellationToken).ConfigureAwait(false);
                        LogAramBenchSwapResult(
                            LogEventLevel.Information,
                            "Succeeded",
                            operationId,
                            context,
                            championId,
                            attemptCount,
                            stopwatch.ElapsedMilliseconds,
                            "Automatic ARAM champion swap request was accepted.");
                        return;
                    }
                    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                    {
                        throw;
                    }
                    catch (Exception exception)
                    {
                        lastError = exception;
                        Log.Debug(exception,
                            "Automatic ARAM bench swap attempt {AttemptCount} failed for champion {ChampionId}",
                            attemptCount, championId);
                    }
                }

                if (lastError is not null &&
                    IsCurrentAramBenchSwapState(context, stateSignature, championId))
                {
                    LogAramBenchSwapResult(
                        LogEventLevel.Error,
                        "Failed",
                        operationId,
                        context,
                        championId,
                        attemptCount,
                        stopwatch.ElapsedMilliseconds,
                        "Automatic ARAM champion swap failed.",
                        lastError);
                    AllowAramBenchSwapRetry(stateSignature);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                LogAramBenchSwapResult(
                    LogEventLevel.Information,
                    "Cancelled",
                    operationId,
                    context,
                    championId,
                    attemptCount,
                    stopwatch.ElapsedMilliseconds,
                    "Automatic ARAM champion swap was cancelled.");
            }
        }

        private void AllowAramBenchSwapRetry(string stateSignature)
        {
            lock (_stateSync)
            {
                if (string.Equals(_lastAramBenchSwapState, stateSignature,
                        StringComparison.Ordinal))
                {
                    if (string.Equals(_aramBenchSwapFailedState, stateSignature,
                            StringComparison.Ordinal))
                    {
                        _aramBenchSwapFailureRetryConsumed = true;
                    }
                    else
                    {
                        _aramBenchSwapFailedState = stateSignature;
                        _aramBenchSwapFailureRetryConsumed = false;
                        _lastAramBenchSwapState = string.Empty;
                    }
                }
            }
        }

        private bool IsCurrentAramBenchSwapState(
            PhaseContext context,
            string stateSignature,
            int championId)
        {
            if (!IsCurrentPhase(context) ||
                context.Phase != GameflowPhase.ChampSelect ||
                !AutomationSettings.AutoSwapAramBench)
            {
                return false;
            }

            var snapshot = GetCurrentSnapshot();
            var preferredChampionIds = AutomationSettings.PreferredAramChampionIds?
                .Where(id => id > 0)
                .Distinct()
                .ToArray() ?? [];
            return string.Equals(
                       stateSignature,
                       BuildAramBenchSwapState(context, snapshot, preferredChampionIds),
                       StringComparison.Ordinal) &&
                   FindPreferredAramBenchChampion(snapshot, preferredChampionIds) == championId;
        }

        private static int FindPreferredAramBenchChampion(
            LiveMatchSnapshot snapshot,
            IReadOnlyList<int> preferredChampionIds)
        {
            var championSelect = snapshot?.ChampionSelect;
            if (championSelect is null || !championSelect.BenchEnabled ||
                preferredChampionIds is null || preferredChampionIds.Count == 0 ||
                !IsAramSession(snapshot))
            {
                return 0;
            }

            var currentChampionId = championSelect.MyTeam?.FirstOrDefault(member =>
                    member?.CellId == championSelect.LocalPlayerCellId)?.ChampionId ?? 0;
            if (currentChampionId <= 0 ||
                preferredChampionIds.Contains(currentChampionId))
            {
                return 0;
            }

            var benchChampionIds = championSelect.BenchChampions?.Where(
                    champion => champion is not null && champion.ChampionId > 0)
                .Select(champion => champion.ChampionId)
                .ToHashSet() ?? [];
            return preferredChampionIds.FirstOrDefault(benchChampionIds.Contains);
        }

        private static bool IsAramSession(LiveMatchSnapshot snapshot)
        {
            return GameModeResolver.IsAram(snapshot);
        }

        private static string BuildAramBenchSwapState(
            PhaseContext context,
            LiveMatchSnapshot snapshot,
            IReadOnlyList<int> preferredChampionIds)
        {
            var championSelect = snapshot?.ChampionSelect;
            var gameData = snapshot?.GameflowSession?.GameData;
            var lobbyConfig = snapshot?.Lobby?.GameConfig;
            var matchmakingQueueId = snapshot?.Matchmaking?.Queue?.Id ?? 0;
            var localChampionId = championSelect?.MyTeam?.FirstOrDefault(member =>
                    member?.CellId == championSelect.LocalPlayerCellId)?.ChampionId ?? 0;
            var builder = new StringBuilder()
                .Append(context.Instance).Append('|')
                .Append(gameData?.QueueId ?? 0).Append('|')
                .Append(gameData?.MapId ?? 0).Append('|')
                .Append(gameData?.GameMode).Append('|')
                .Append(lobbyConfig?.QueueId ?? 0).Append('|')
                .Append(lobbyConfig?.MapId ?? 0).Append('|')
                .Append(lobbyConfig?.GameMode).Append('|')
                .Append(matchmakingQueueId).Append('|')
                .Append(championSelect?.BenchEnabled ?? false).Append('|')
                .Append(localChampionId).Append('|');

            foreach (var champion in championSelect?.BenchChampions ?? [])
            {
                builder.Append(champion?.ChampionId ?? 0).Append(',');
            }

            builder.Append('|');
            foreach (var championId in preferredChampionIds ?? [])
            {
                builder.Append(championId).Append(',');
            }

            return builder.ToString();
        }

        private void CancelAutoAramBenchSwap(bool resetState)
        {
            CancellationTokenSource cancellationTokenSource;
            lock (_stateSync)
            {
                cancellationTokenSource = _aramBenchSwapAutomationCts;
                _aramBenchSwapAutomationCts = null;
                if (resetState)
                {
                    _lastAramBenchSwapState = string.Empty;
                    _aramBenchSwapFailedState = string.Empty;
                    _aramBenchSwapFailureRetryConsumed = false;
                }
            }

            cancellationTokenSource?.Cancel();
            cancellationTokenSource?.Dispose();
        }

        private static void LogAramBenchSwapResult(
            LogEventLevel level,
            string outcome,
            Guid operationId,
            PhaseContext context,
            int championId,
            int attemptCount,
            long durationMs,
            string displayMessage,
            Exception exception = null)
        {
            var properties = new Dictionary<string, object>
            {
                ["TargetType"] = "Champion",
                ["TargetId"] = championId,
                ["GameflowPhase"] = context.RawPhase,
                ["PhaseInstance"] = context.Instance,
                ["AttemptCount"] = attemptCount,
                ["DurationMs"] = durationMs
            };
            if (exception is HttpRequestException httpException &&
                httpException.StatusCode.HasValue)
            {
                properties["HttpStatusCode"] = (int)httpException.StatusCode.Value;
            }

            if (exception is not null)
            {
                properties["ErrorType"] = exception.GetType().Name;
            }

            OperationLog.Write(
                level,
                "champ_select.bench.swap",
                "ChampionSelect",
                "Automation",
                outcome,
                operationId,
                "MatchService",
                displayMessage,
                properties,
                exception);
        }

        private void StartAutoChampionSelectAction(PhaseContext context)
        {
            var championSelect = GetCurrentSnapshot().ChampionSelect;
            var action = FindActiveLocalChampionSelectAction(championSelect);
            if (action is null)
            {
                CancelAutoChampionSelectAction(resetState: true);
                return;
            }

            var isPick = string.Equals(
                action.Type, "pick", StringComparison.OrdinalIgnoreCase);
            var enabled = isPick
                ? AutomationSettings.AutoPickChampion
                : AutomationSettings.AutoBanChampion;
            if (!enabled)
            {
                CancelAutoChampionSelectAction(resetState: true);
                return;
            }

            var preferredChampionIds = (isPick
                    ? AutomationSettings.PreferredPickChampionIds
                    : AutomationSettings.PreferredBanChampionIds)?
                .Where(championId => championId > 0)
                .Distinct()
                .ToArray() ?? [];
            var stateSignature = BuildChampionSelectAutomationState(
                context, championSelect, action, preferredChampionIds);

            CancellationTokenSource previousCts;
            lock (_stateSync)
            {
                if (!_started || context.Version != _phaseVersion ||
                    context.Token.IsCancellationRequested ||
                    string.Equals(
                        _lastChampionSelectAutomationState,
                        stateSignature,
                        StringComparison.Ordinal))
                {
                    return;
                }

                previousCts = _championSelectAutomationCts;
                _championSelectAutomationCts = null;
                _lastChampionSelectAutomationState = stateSignature;

                if (preferredChampionIds.Length > 0)
                {
                    _championSelectAutomationCts =
                        CancellationTokenSource.CreateLinkedTokenSource(context.Token);
                    var token = _championSelectAutomationCts.Token;
                    _championSelectAutomationTask = RunAutoChampionSelectActionAsync(
                        context,
                        action,
                        preferredChampionIds,
                        token);
                }
            }

            previousCts?.Cancel();
            previousCts?.Dispose();
        }

        private async Task RunAutoChampionSelectActionAsync(
            PhaseContext context,
            ChampionSelectActionSnapshot action,
            IReadOnlyList<int> preferredChampionIds,
            CancellationToken cancellationToken)
        {
            var operationId = Guid.NewGuid();
            var stopwatch = Stopwatch.StartNew();
            var attemptCount = 0;
            var isPick = string.Equals(
                action.Type, "pick", StringComparison.OrdinalIgnoreCase);
            Exception lastError = null;
            var lastChampionId = 0;

            try
            {
                if (!IsCurrentChampionSelectAction(context, action))
                {
                    return;
                }

                if (!_leagueClient.Connected || !_httpService.IsInitialized)
                {
                    LogChampionSelectAutomationResult(
                        LogEventLevel.Warning,
                        "Rejected",
                        operationId,
                        context,
                        action,
                        0,
                        attemptCount,
                        stopwatch.ElapsedMilliseconds,
                        "Automatic champion-select action was rejected because LCU is unavailable.");
                    return;
                }

                IReadOnlyList<int> availableChampionIds = isPick
                    ? await _gameService.GetPickableChampionIdsAsync(cancellationToken)
                        .ConfigureAwait(false)
                    : await _gameService.GetBannableChampionIdsAsync(cancellationToken)
                        .ConfigureAwait(false);
                var candidates = FindChampionSelectCandidates(
                    GetCurrentSnapshot().ChampionSelect,
                    action,
                    preferredChampionIds,
                    availableChampionIds)
                    .Take(ChampionSelectActionRetryDelays.Length)
                    .ToArray();
                if (candidates.Length == 0)
                {
                    LogChampionSelectAutomationResult(
                        LogEventLevel.Warning,
                        "Skipped",
                        operationId,
                        context,
                        action,
                        0,
                        attemptCount,
                        stopwatch.ElapsedMilliseconds,
                        isPick
                            ? "Automatic champion picking was skipped because no preferred champion is available."
                            : "Automatic champion banning was skipped because no preferred champion is available.");
                    return;
                }

                for (var candidateIndex = 0;
                     candidateIndex < candidates.Length;
                     candidateIndex++)
                {
                    var delay = ChampionSelectActionRetryDelays[candidateIndex];
                    if (delay > TimeSpan.Zero)
                    {
                        await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
                    }

                    if (!IsCurrentChampionSelectAction(context, action))
                    {
                        LogChampionSelectAutomationResult(
                            LogEventLevel.Information,
                            "Cancelled",
                            operationId,
                            context,
                            action,
                            lastChampionId,
                            attemptCount,
                            stopwatch.ElapsedMilliseconds,
                            "Automatic champion-select action was cancelled because the active turn changed.");
                        return;
                    }

                    lastChampionId = candidates[candidateIndex];
                    attemptCount++;
                    try
                    {
                        await _gameService.CompleteChampionSelectActionAsync(
                                action, lastChampionId, cancellationToken)
                            .ConfigureAwait(false);
                        LogChampionSelectAutomationResult(
                            LogEventLevel.Information,
                            "Succeeded",
                            operationId,
                            context,
                            action,
                            lastChampionId,
                            attemptCount,
                            stopwatch.ElapsedMilliseconds,
                            isPick
                                ? "Automatic champion pick request was accepted."
                                : "Automatic champion ban request was accepted.");
                        return;
                    }
                    catch (OperationCanceledException) when (
                        cancellationToken.IsCancellationRequested)
                    {
                        throw;
                    }
                    catch (Exception exception)
                    {
                        lastError = exception;
                        Log.Debug(
                            exception,
                            "Automatic champion-select attempt {AttemptCount} failed for action {ActionId} and champion {ChampionId}",
                            attemptCount,
                            action.Id,
                            lastChampionId);
                    }
                }

                if (lastError is not null &&
                    IsCurrentChampionSelectAction(context, action))
                {
                    LogChampionSelectAutomationResult(
                        LogEventLevel.Error,
                        "Failed",
                        operationId,
                        context,
                        action,
                        lastChampionId,
                        attemptCount,
                        stopwatch.ElapsedMilliseconds,
                        isPick
                            ? "Automatic champion picking failed."
                            : "Automatic champion banning failed.",
                        lastError);
                }
            }
            catch (OperationCanceledException) when (
                cancellationToken.IsCancellationRequested)
            {
                LogChampionSelectAutomationResult(
                    LogEventLevel.Information,
                    "Cancelled",
                    operationId,
                    context,
                    action,
                    lastChampionId,
                    attemptCount,
                    stopwatch.ElapsedMilliseconds,
                    "Automatic champion-select action was cancelled.");
            }
            catch (Exception exception)
            {
                LogChampionSelectAutomationResult(
                    LogEventLevel.Error,
                    "Failed",
                    operationId,
                    context,
                    action,
                    lastChampionId,
                    attemptCount,
                    stopwatch.ElapsedMilliseconds,
                    isPick
                        ? "Automatic champion picking failed."
                        : "Automatic champion banning failed.",
                    exception);
            }
        }

        private bool IsCurrentChampionSelectAction(
            PhaseContext context,
            ChampionSelectActionSnapshot expectedAction)
        {
            if (!IsCurrentPhase(context) ||
                context.Phase != GameflowPhase.ChampSelect)
            {
                return false;
            }

            var currentAction = FindActiveLocalChampionSelectAction(
                GetCurrentSnapshot().ChampionSelect);
            if (currentAction is null ||
                currentAction.Id != expectedAction.Id ||
                !string.Equals(
                    currentAction.Type,
                    expectedAction.Type,
                    StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            return string.Equals(
                currentAction.Type, "pick", StringComparison.OrdinalIgnoreCase)
                ? AutomationSettings.AutoPickChampion
                : AutomationSettings.AutoBanChampion;
        }

        private static ChampionSelectActionSnapshot FindActiveLocalChampionSelectAction(
            ChampionSelectSnapshot championSelect)
        {
            if (championSelect is null)
            {
                return null;
            }

            return championSelect.Actions?
                .Where(round => round is not null)
                .SelectMany(round => round)
                .FirstOrDefault(action =>
                    action is not null &&
                    action.ActorCellId == championSelect.LocalPlayerCellId &&
                    action.IsInProgress &&
                    !action.Completed &&
                    (string.Equals(
                         action.Type, "pick", StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(
                         action.Type, "ban", StringComparison.OrdinalIgnoreCase)));
        }

        private static IEnumerable<int> FindChampionSelectCandidates(
            ChampionSelectSnapshot championSelect,
            ChampionSelectActionSnapshot action,
            IReadOnlyList<int> preferredChampionIds,
            IReadOnlyList<int> availableChampionIds)
        {
            var available = availableChampionIds?
                .Where(championId => championId > 0)
                .ToHashSet() ?? [];
            if (available.Count == 0)
            {
                return [];
            }

            var unavailable = new HashSet<int>();
            foreach (var championId in championSelect?.Bans?.MyTeamBans ?? [])
            {
                unavailable.Add(championId);
            }
            foreach (var championId in championSelect?.Bans?.TheirTeamBans ?? [])
            {
                unavailable.Add(championId);
            }

            if (string.Equals(action.Type, "ban", StringComparison.OrdinalIgnoreCase))
            {
                foreach (var member in championSelect?.MyTeam ?? [])
                {
                    if (member?.ChampionPickIntent > 0)
                    {
                        unavailable.Add(member.ChampionPickIntent);
                    }
                }
            }

            return preferredChampionIds?
                .Where(championId =>
                    championId > 0 &&
                    available.Contains(championId) &&
                    !unavailable.Contains(championId))
                .Distinct() ?? [];
        }

        private static string BuildChampionSelectAutomationState(
            PhaseContext context,
            ChampionSelectSnapshot championSelect,
            ChampionSelectActionSnapshot action,
            IReadOnlyList<int> preferredChampionIds)
        {
            var builder = new StringBuilder()
                .Append(context.Instance).Append('|')
                .Append(action.Id).Append('|')
                .Append(action.Type).Append('|')
                .Append(action.ChampionId).Append('|')
                .Append(action.IsInProgress).Append('|')
                .Append(action.Completed).Append('|');

            foreach (var member in championSelect?.MyTeam ?? [])
            {
                builder.Append(member?.CellId ?? 0).Append(':')
                    .Append(member?.ChampionId ?? 0).Append(':')
                    .Append(member?.ChampionPickIntent ?? 0).Append(',');
            }

            builder.Append('|');
            foreach (var championId in championSelect?.Bans?.MyTeamBans ?? [])
            {
                builder.Append(championId).Append(',');
            }
            foreach (var championId in championSelect?.Bans?.TheirTeamBans ?? [])
            {
                builder.Append(championId).Append(',');
            }

            builder.Append('|');
            foreach (var championId in preferredChampionIds ?? [])
            {
                builder.Append(championId).Append(',');
            }

            return builder.ToString();
        }

        private void CancelAutoChampionSelectAction(bool resetState)
        {
            CancellationTokenSource cancellationTokenSource;
            lock (_stateSync)
            {
                cancellationTokenSource = _championSelectAutomationCts;
                _championSelectAutomationCts = null;
                if (resetState)
                {
                    _lastChampionSelectAutomationState = string.Empty;
                }
            }

            cancellationTokenSource?.Cancel();
            cancellationTokenSource?.Dispose();
        }

        private static void LogChampionSelectAutomationResult(
            LogEventLevel level,
            string outcome,
            Guid operationId,
            PhaseContext context,
            ChampionSelectActionSnapshot action,
            int championId,
            int attemptCount,
            long durationMs,
            string displayMessage,
            Exception exception = null)
        {
            var isPick = string.Equals(
                action.Type, "pick", StringComparison.OrdinalIgnoreCase);
            var properties = new Dictionary<string, object>
            {
                ["ActionId"] = action.Id,
                ["ChampionId"] = championId,
                ["TargetType"] = "Champion",
                ["TargetId"] = championId,
                ["GameflowPhase"] = context.RawPhase,
                ["PhaseInstance"] = context.Instance,
                ["AttemptCount"] = attemptCount,
                ["DurationMs"] = durationMs
            };
            if (exception is HttpRequestException httpException &&
                httpException.StatusCode.HasValue)
            {
                properties["HttpStatusCode"] = (int)httpException.StatusCode.Value;
            }
            if (exception is not null)
            {
                properties["ErrorType"] = exception.GetType().Name;
            }

            OperationLog.Write(
                level,
                isPick ? "champ_select.pick" : "champ_select.ban",
                "ChampionSelect",
                "Automation",
                outcome,
                operationId,
                "MatchService",
                displayMessage,
                properties,
                exception);
        }

        private void StartAutoAccept(PhaseContext context)
        {
            CancellationToken token;
            lock (_stateSync)
            {
                if (!_started || _lastAutoAcceptInstance == context.Instance ||
                    !AutomationSettings.AutoAcceptReadyCheck)
                {
                    return;
                }

                _lastAutoAcceptInstance = context.Instance;
                _acceptAutomationCts?.Cancel();
                _acceptAutomationCts?.Dispose();
                _acceptAutomationCts = CancellationTokenSource.CreateLinkedTokenSource(context.Token);
                token = _acceptAutomationCts.Token;
                _acceptAutomationTask = RunAutomationAsync(
                    "Automatic ready-check acceptance", context,
                    AcceptRetryDelays, _gameService.AcceptMatchAsync, token);
            }
        }

        private void StartAutoReconnect(PhaseContext context)
        {
            if (!CanAutoReconnect(context))
            {
                return;
            }

            CancellationToken token;
            lock (_stateSync)
            {
                if (!_started || context.Version != _phaseVersion ||
                    context.Token.IsCancellationRequested ||
                    _lastAutoReconnectInstance == context.Instance ||
                    !AutomationSettings.AutoReconnect)
                {
                    return;
                }

                _lastAutoReconnectInstance = context.Instance;
                _reconnectAutomationCts?.Cancel();
                _reconnectAutomationCts?.Dispose();
                _reconnectAutomationCts = CancellationTokenSource.CreateLinkedTokenSource(context.Token);
                token = _reconnectAutomationCts.Token;
                _reconnectAutomationTask = RunAutomationAsync(
                    "Automatic game reconnect", context,
                    ReconnectRetryDelays,
                    cancellationToken => ReconnectGameIfConfirmedAsync(
                        context, cancellationToken),
                    token,
                    () => CanAutoReconnect(context));
            }
        }

        private async Task ReconnectGameIfConfirmedAsync(
            PhaseContext context,
            CancellationToken cancellationToken)
        {
            if (!CanAutoReconnect(context) || !_leagueClient.Connected ||
                !_httpService.IsInitialized)
            {
                return;
            }

            var phaseTask = _gameService.GetGameflowPhaseAsync(cancellationToken);
            var sessionTask = _gameService.GetGameflowSessionSnapshotAsync(cancellationToken);
            await Task.WhenAll(phaseTask, sessionTask).ConfigureAwait(false);

            var rawPhase = await phaseTask.ConfigureAwait(false);
            var session = await sessionTask.ConfigureAwait(false);
            if (ParsePhase(rawPhase) != GameflowPhase.Reconnect ||
                ParsePhase(session?.Phase) != GameflowPhase.Reconnect ||
                session?.GameClient?.Running != true)
            {
                return;
            }

            if (!CanAutoReconnect(context) || !_leagueClient.Connected ||
                !_httpService.IsInitialized)
            {
                return;
            }

            await _gameService.ReconnectGameAsync(cancellationToken).ConfigureAwait(false);
        }

        private bool CanAutoReconnect(PhaseContext context)
        {
            var snapshot = GetCurrentSnapshot();
            return context.Phase == GameflowPhase.Reconnect &&
                   snapshot.GameflowPhase == GameflowPhase.Reconnect &&
                   ParsePhase(snapshot.GameflowSession?.Phase) == GameflowPhase.Reconnect &&
                   snapshot.GameflowSession?.GameClient?.Running == true;
        }

        private void CancelAutoReconnect()
        {
            CancellationTokenSource cancellationTokenSource;
            lock (_stateSync)
            {
                cancellationTokenSource = _reconnectAutomationCts;
            }

            cancellationTokenSource?.Cancel();
        }

        private async Task RunAutomationAsync(string operationName, PhaseContext context,
            IReadOnlyList<TimeSpan> retryDelays, Func<CancellationToken, Task> operation,
            CancellationToken cancellationToken, Func<bool> canExecute = null)
        {
            Exception lastError = null;
            for (var attempt = 0; attempt < retryDelays.Count; attempt++)
            {
                if (retryDelays[attempt] > TimeSpan.Zero)
                {
                    await Task.Delay(retryDelays[attempt], cancellationToken).ConfigureAwait(false);
                }

                if (!IsCurrentPhase(context) || canExecute?.Invoke() == false)
                {
                    return;
                }

                try
                {
                    await operation(cancellationToken).ConfigureAwait(false);
                    return;
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception exception)
                {
                    lastError = exception;
                }
            }

            if (lastError is not null && IsCurrentPhase(context))
            {
                PublishError($"{operationName} failed after {retryDelays.Count} attempts.",
                    lastError, false);
            }
        }

        private void HandleAutomationSettingsChanged(object sender, PropertyChangedEventArgs args)
        {
            if (args.PropertyName is nameof(IGameAutomationSettings.AutoAcceptReadyCheck)
                or nameof(IGameAutomationSettings.AutoAccept)
                or nameof(IGameAutomationSettings.IsAutoAcceptEnabled))
            {
                if (!AutomationSettings.AutoAcceptReadyCheck)
                {
                    _acceptAutomationCts?.Cancel();
                }
                else
                {
                    TriggerAutomation(GetCurrentPhaseContext());
                }
            }

            if (args.PropertyName is nameof(IGameAutomationSettings.AutoReconnect)
                or nameof(IGameAutomationSettings.IsAutoReconnectEnabled))
            {
                if (!AutomationSettings.AutoReconnect)
                {
                    _reconnectAutomationCts?.Cancel();
                }
                else
                {
                    TriggerAutomation(GetCurrentPhaseContext());
                }
            }

            if (args.PropertyName is nameof(IGameAutomationSettings.AutoSwapAramBench)
                or nameof(IGameAutomationSettings.PreferredAramChampionIds))
            {
                CancelAutoAramBenchSwap(resetState: true);
                if (AutomationSettings.AutoSwapAramBench)
                {
                    TriggerAutomation(GetCurrentPhaseContext());
                }
            }


            if (args.PropertyName is nameof(IGameAutomationSettings.AutoPickChampion)
                or nameof(IGameAutomationSettings.AutoBanChampion)
                or nameof(IGameAutomationSettings.PreferredPickChampionIds)
                or nameof(IGameAutomationSettings.PreferredBanChampionIds))
            {
                CancelAutoChampionSelectAction(resetState: true);
                if (AutomationSettings.AutoPickChampion ||
                    AutomationSettings.AutoBanChampion)
                {
                    TriggerAutomation(GetCurrentPhaseContext());
                }
            }
        }

        private void PublishResourceUpdate(Func<LiveMatchSnapshot, LiveMatchSnapshot> update)
        {
            PublishSnapshot(snapshot =>
            {
                var next = CopySnapshot(snapshot);
                next = update(next);
                next.Error = string.Empty;
                next.Errors = Array.Empty<string>();
                next.DataQuality = _leagueClient.Connected
                    ? DataQuality.Complete
                    : DataQuality.Stale;
                ApplyRosterDataQuality(next);
                return next;
            });
        }

        private void PublishError(string message, Exception exception, bool connectionError)
        {
            var detail = exception is null ? message : $"{message} {exception.Message}";
            PublishSnapshot(snapshot =>
            {
                var next = CopySnapshot(snapshot);
                next.Error = detail;
                next.Errors = [detail];
                next.DataQuality = HasAnyData(next) ? DataQuality.Partial : DataQuality.Error;
                if (connectionError)
                {
                    next.ConnectionState = ConnectionState.Error;
                }

                return next;
            }, new SnapshotErrorLogContext(
                message,
                exception is null ? Array.Empty<Exception>() : [exception]));
        }

        private void PublishSnapshot(Func<LiveMatchSnapshot, LiveMatchSnapshot> update,
            SnapshotErrorLogContext errorLogContext = null)
        {
            lock (_publicationSync)
            {
                LiveMatchSnapshot next;
                bool shouldLogError;
                lock (_snapshotSync)
                {
                    var current = _current;
                    next = update(current);
                    if (next is null || ReferenceEquals(next, current))
                    {
                        return;
                    }
                    next.Version = ++_snapshotVersion;
                    next.UpdatedAt = DateTimeOffset.UtcNow;
                    shouldLogError = !string.IsNullOrWhiteSpace(next.Error) &&
                                     !string.Equals(current.Error, next.Error,
                                         StringComparison.Ordinal);
                    _current = next;
                }

                if (shouldLogError)
                {
                    var safeError = errorLogContext?.SafeMessage ??
                                    LiveMatchSnapshotErrorLog.SanitizeStateError(
                                        next.Error, _leagueClient.Token);
                    LiveMatchSnapshotErrorLog.Write(
                        _logger,
                        next,
                        safeError,
                        errorLogContext?.Exceptions);
                }

                // Keep version assignment and observer notification in one
                // serialized publication lane. Consumers therefore never see
                // a newer version before an older one from another thread.
                var handlers = SnapshotChanged;
                if (handlers is null)
                {
                    return;
                }

                // Serialize the immutable publication once. Each observer still receives
                // an independent object graph, so a misbehaving consumer cannot mutate
                // service state or another observer's snapshot.
                var serializedSnapshot = SerializeForConsumer(next);

                foreach (EventHandler<LiveMatchSnapshotChangedEventArgs> handler in
                         handlers.GetInvocationList())
                {
                    try
                    {
                        var args = new LiveMatchSnapshotChangedEventArgs(
                            DeserializeForConsumer(serializedSnapshot));
                        handler(this, args);
                    }
                    catch (Exception exception)
                    {
                        // Snapshot publication is not allowed to break the
                        // websocket or retry loop because one observer failed.
                        Log.Error(exception,
                            "A live-match snapshot observer failed for version {Version}",
                            next.Version);
                    }
                }
            }
        }

        private static LiveMatchSnapshot CopySnapshot(LiveMatchSnapshot source)
        {
            return new LiveMatchSnapshot
            {
                Version = source.Version,
                ConnectionState = source.ConnectionState,
                GameflowPhase = source.GameflowPhase,
                RawPhase = source.RawPhase,
                GameflowSession = source.GameflowSession,
                Lobby = source.Lobby,
                Matchmaking = source.Matchmaking,
                ReadyCheck = source.ReadyCheck,
                ChampionSelect = source.ChampionSelect,
                PostGame = source.PostGame,
                Roster = CloneRoster(source.Roster),
                UpdatedAt = source.UpdatedAt,
                DataQuality = source.DataQuality,
                Error = source.Error,
                Errors = source.Errors ?? Array.Empty<string>()
            };
        }

        private static LiveMatchRosterSnapshot CloneRoster(
            LiveMatchRosterSnapshot source)
        {
            if (source is null)
            {
                return null;
            }

            return new LiveMatchRosterSnapshot
            {
                GameId = source.GameId,
                SourcePhase = source.SourcePhase,
                Signature = source.Signature,
                IsResolving = source.IsResolving,
                MyTeam = (source.MyTeam ?? Array.Empty<LiveMatchPlayerSnapshot>())
                    .Select(ClonePlayer)
                    .ToArray(),
                TheirTeam = (source.TheirTeam ?? Array.Empty<LiveMatchPlayerSnapshot>())
                    .Select(ClonePlayer)
                    .ToArray()
            };
        }

        private static LiveMatchPlayerSnapshot ClonePlayer(
            LiveMatchPlayerSnapshot source)
        {
            if (source is null)
            {
                return null;
            }

            return new LiveMatchPlayerSnapshot
            {
                Slot = source.Slot,
                CellId = source.CellId,
                ChampionId = source.ChampionId,
                Spell1Id = source.Spell1Id,
                Spell2Id = source.Spell2Id,
                ChampionIcon = source.ChampionIcon,
                Spell1Icon = source.Spell1Icon,
                Spell2Icon = source.Spell2Icon,
                Puuid = source.Puuid,
                Position = source.Position,
                DisplayName = source.DisplayName,
                IsLocalPlayer = source.IsLocalPlayer,
                IsHidden = source.IsHidden,
                IsPlaceholder = source.IsPlaceholder,
                DataState = source.DataState,
                Summoner = source.Summoner,
                SoloRank = source.SoloRank,
                RecentWins = source.RecentWins,
                RecentLosses = source.RecentLosses,
                RecentMatchCount = source.RecentMatchCount,
                AverageKda = source.AverageKda,
                RecentResults = source.RecentResults?.ToArray() ?? Array.Empty<bool>(),
                RecentMatches = (source.RecentMatches ??
                        Array.Empty<LiveMatchRecentMatchSnapshot>())
                    .Select(CloneRecentMatch)
                    .ToArray(),
                Error = source.Error
            };
        }

        private static LiveMatchRecentMatchSnapshot CloneRecentMatch(
            LiveMatchRecentMatchSnapshot source)
        {
            if (source is null)
            {
                return null;
            }

            return new LiveMatchRecentMatchSnapshot
            {
                GameId = source.GameId,
                GameCreation = source.GameCreation,
                QueueId = source.QueueId,
                GameMode = source.GameMode,
                ChampionId = source.ChampionId,
                ChampionIcon = source.ChampionIcon,
                IsWin = source.IsWin,
                Kills = source.Kills,
                Deaths = source.Deaths,
                Assists = source.Assists
            };
        }

        private static LiveMatchSnapshot PrepareForPhase(LiveMatchSnapshot source,
            GameflowPhase phase, string rawPhase)
        {
            var next = CopySnapshot(source);
            next.GameflowPhase = phase;
            next.RawPhase = rawPhase;
            next.Error = string.Empty;
            next.Errors = Array.Empty<string>();
            next.DataQuality = source.ConnectionState == ConnectionState.Connected
                ? DataQuality.Partial
                : source.DataQuality;

            if (!IsRosterPhase(phase))
            {
                next.Roster = null;
            }

            switch (phase)
            {
                case GameflowPhase.None:
                    next.Lobby = null;
                    next.Matchmaking = null;
                    next.ReadyCheck = null;
                    next.ChampionSelect = null;
                    break;
                case GameflowPhase.Lobby:
                    next.Matchmaking = null;
                    next.ReadyCheck = null;
                    next.ChampionSelect = null;
                    next.PostGame = null;
                    break;
                case GameflowPhase.Matchmaking:
                    next.ReadyCheck = null;
                    next.ChampionSelect = null;
                    next.PostGame = null;
                    break;
                case GameflowPhase.ReadyCheck:
                    next.ChampionSelect = null;
                    next.PostGame = null;
                    break;
                case GameflowPhase.ChampSelect:
                case GameflowPhase.GameStart:
                case GameflowPhase.InProgress:
                case GameflowPhase.Reconnect:
                    next.ReadyCheck = null;
                    next.PostGame = null;
                    break;
                case GameflowPhase.WaitingForStats:
                case GameflowPhase.PreEndOfGame:
                case GameflowPhase.EndOfGame:
                    next.Lobby = null;
                    next.Matchmaking = null;
                    next.ReadyCheck = null;
                    next.ChampionSelect = null;
                    break;
            }

            return next;
        }

        private static GameflowPhase ParsePhase(string rawPhase)
        {
            var normalized = new string((rawPhase ?? string.Empty)
                .Where(char.IsLetterOrDigit)
                .Select(char.ToLowerInvariant)
                .ToArray());

            return normalized switch
            {
                "none" => GameflowPhase.None,
                "lobby" => GameflowPhase.Lobby,
                "matchmaking" => GameflowPhase.Matchmaking,
                "readycheck" => GameflowPhase.ReadyCheck,
                "champselect" => GameflowPhase.ChampSelect,
                "gamestart" => GameflowPhase.GameStart,
                "inprogress" => GameflowPhase.InProgress,
                "waitingforstats" => GameflowPhase.WaitingForStats,
                "preendofgame" => GameflowPhase.PreEndOfGame,
                "endofgame" => GameflowPhase.EndOfGame,
                "reconnect" => GameflowPhase.Reconnect,
                "terminatedinerror" => GameflowPhase.TerminatedInError,
                _ => GameflowPhase.Unknown
            };
        }

        private static bool IsPostGamePhase(GameflowPhase phase)
        {
            return phase is GameflowPhase.WaitingForStats
                or GameflowPhase.PreEndOfGame
                or GameflowPhase.EndOfGame;
        }

        private static bool HasAnyData(LiveMatchSnapshot snapshot)
        {
            return snapshot.GameflowSession is not null || snapshot.Lobby is not null ||
                   snapshot.Matchmaking is not null || snapshot.ReadyCheck is not null ||
                   snapshot.ChampionSelect is not null || snapshot.PostGame is not null ||
                   !string.IsNullOrWhiteSpace(snapshot.RawPhase);
        }

        private static bool IsDelete(OnWebsocketEventArgs args)
        {
            return string.Equals(args?.EventType, "Delete", StringComparison.OrdinalIgnoreCase);
        }

        private static string ReadPhase(object data)
        {
            if (data is null)
            {
                return string.Empty;
            }

            if (data is JValue value)
            {
                return value.Value<string>() ?? string.Empty;
            }

            if (data is JToken token)
            {
                return token.Type == JTokenType.String
                    ? token.Value<string>() ?? string.Empty
                    : token.ToString().Trim().Trim('"');
            }

            return data.ToString()?.Trim().Trim('"') ?? string.Empty;
        }

        private static T DeserializeEvent<T>(object data) where T : class
        {
            if (data is null)
            {
                return null;
            }

            try
            {
                if (data is JToken token)
                {
                    return token.ToObject<T>();
                }

                return JsonConvert.DeserializeObject<T>(JsonConvert.SerializeObject(data));
            }
            catch (JsonException)
            {
                return null;
            }
        }

        private static void NormalizeChampionSelect(ChampionSelectSnapshot value)
        {
            if (value is null)
            {
                return;
            }

            value.Actions ??= [];
            for (var index = 0; index < value.Actions.Count; index++)
            {
                value.Actions[index] ??= [];
            }

            value.MyTeam ??= [];
            value.TheirTeam ??= [];
            value.BenchChampions ??= [];
            foreach (var enemy in value.TheirTeam.Where(enemy => enemy is not null))
            {
                // Champion-select opponents are intentionally published only
                // as privacy placeholders, even if a client build happens to
                // include identity fields in the payload.
                enemy.Puuid = string.Empty;
                enemy.SummonerId = 0;
                enemy.ObfuscatedPuuid = string.Empty;
                enemy.ObfuscatedSummonerId = 0;
                enemy.NameVisibilityType = "HIDDEN";
            }
        }

        private static async Task<FetchResult<T>> FetchAsync<T>(string name,
            Func<Task<T>> fetch, CancellationToken cancellationToken) where T : class
        {
            try
            {
                var value = await fetch().ConfigureAwait(false);
                return FetchResult<T>.Success(value);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (HttpRequestException exception) when (exception.StatusCode is HttpStatusCode.NotFound
                                                         or HttpStatusCode.NoContent)
            {
                return FetchResult<T>.Unavailable();
            }
            catch (Exception exception)
            {
                return FetchResult<T>.Failure(FormatError(name, exception), exception);
            }
        }

        private async Task<FetchResult<PostGameSnapshot>> FetchPostGameAsync(
            PhaseContext context, CancellationToken cancellationToken)
        {
            // The end-of-game endpoint can lag slightly behind the phase event.
            // Keep this bounded and phase-cancellable so one early 404/null does
            // not leave the result page empty for the rest of the lifecycle.
            var delays = context.Phase == GameflowPhase.EndOfGame
                ? new[] { TimeSpan.Zero, TimeSpan.FromMilliseconds(300),
                    TimeSpan.FromMilliseconds(700), TimeSpan.FromMilliseconds(1400) }
                : new[] { TimeSpan.Zero };
            FetchResult<PostGameSnapshot> result = null;
            foreach (var delay in delays)
            {
                if (delay > TimeSpan.Zero)
                {
                    await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
                }

                if (!IsCurrentPhase(context))
                {
                    return FetchResult<PostGameSnapshot>.Unavailable();
                }

                result = await FetchAsync("post-game",
                    () => _gameService.GetPostGameSnapshotAsync(cancellationToken),
                    cancellationToken).ConfigureAwait(false);
                if (result.Value is not null)
                {
                    return result;
                }
            }

            return result ?? FetchResult<PostGameSnapshot>.Unavailable();
        }

        private void SchedulePostGameChampionIconEnrichment(PostGameSnapshot postGame)
        {
            CancellationToken lifetimeToken;
            lock (_stateSync)
            {
                if (!_started || _lifetimeCts is null)
                {
                    return;
                }

                lifetimeToken = _lifetimeCts.Token;
            }

            _ = EnrichPostGameChampionIconsSafelyAsync(postGame, lifetimeToken);
        }

        private async Task EnrichPostGameChampionIconsSafelyAsync(
            PostGameSnapshot postGame, CancellationToken cancellationToken)
        {
            var players = (postGame.Teams ?? [])
                .Where(team => team is not null)
                .SelectMany(team => team.Players ?? [])
                .Where(player => player is not null)
                .Concat(postGame.LocalPlayer is null
                    ? []
                    : [postGame.LocalPlayer])
                .ToArray();
            var championIds = players
                .Where(player => player.ChampionId > 0 &&
                    string.IsNullOrWhiteSpace(player.ChampionIcon))
                .Select(player => player.ChampionId)
                .Distinct()
                .ToArray();
            if (championIds.Length == 0)
            {
                return;
            }

            try
            {
                var iconTasks = championIds.ToDictionary(
                    championId => championId,
                    GetRecentChampionIconAsync);
                await Task.WhenAll(iconTasks.Values).ConfigureAwait(false);
                if (cancellationToken.IsCancellationRequested)
                {
                    return;
                }

                var icons = iconTasks.ToDictionary(
                    pair => pair.Key,
                    pair => pair.Value.Result ?? string.Empty);
                PublishSnapshot(snapshot =>
                {
                    if (snapshot.PostGame is null ||
                        (postGame.GameId > 0 &&
                         snapshot.PostGame.GameId != postGame.GameId))
                    {
                        return snapshot;
                    }

                    var next = CloneForConsumer(snapshot);
                    ApplyPostGameChampionIcons(next.PostGame, icons);
                    return next;
                });
            }
            catch (Exception exception)
            {
                _logger.Debug(exception,
                    "Unable to enrich post-game champion icons for game {GameId}",
                    postGame.GameId);
            }
        }

        private static void ApplyPostGameChampionIcons(PostGameSnapshot postGame,
            IReadOnlyDictionary<int, string> icons)
        {
            var players = (postGame.Teams ?? [])
                .Where(team => team is not null)
                .SelectMany(team => team.Players ?? [])
                .Where(player => player is not null)
                .Concat(postGame.LocalPlayer is null
                    ? []
                    : [postGame.LocalPlayer]);
            foreach (var player in players)
            {
                if (string.IsNullOrWhiteSpace(player.ChampionIcon) &&
                    icons.TryGetValue(player.ChampionId, out var icon))
                {
                    player.ChampionIcon = icon ?? string.Empty;
                }
            }
        }

        private static string FormatError(string name, Exception exception)
        {
            return $"{name}: {exception.Message}";
        }

        private static void AddError(ICollection<string> errors, string error)
        {
            if (!string.IsNullOrWhiteSpace(error) && !errors.Contains(error))
            {
                errors.Add(error);
            }
        }

        private static void AddRefreshFailure(ICollection<string> resources,
            ICollection<Exception> exceptions, string resource, Exception exception)
        {
            if (exception is null)
            {
                return;
            }

            resources.Add(resource);
            exceptions.Add(exception);
        }

        private static async Task AwaitAutomationTaskAsync(Task task)
        {
            if (task is null)
            {
                return;
            }

            try
            {
                await task.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
        }

        private sealed class WritablePropertiesContractResolver : DefaultContractResolver
        {
            protected override IList<JsonProperty> CreateProperties(Type type,
                MemberSerialization memberSerialization)
            {
                return base.CreateProperties(type, memberSerialization)
                    .Where(property => property.Writable)
                    .ToList();
            }
        }

        private readonly struct PhaseContext
        {
            public PhaseContext(long version, long instance, GameflowPhase phase,
                string rawPhase, CancellationToken token)
            {
                Version = version;
                Instance = instance;
                Phase = phase;
                RawPhase = rawPhase;
                Token = token;
            }

            public long Version { get; }

            public long Instance { get; }

            public GameflowPhase Phase { get; }

            public string RawPhase { get; }

            public CancellationToken Token { get; }
        }

        private readonly record struct PhaseTransitionResult(
            PhaseContext Context,
            bool Changed);

        private sealed record PlayerPerformanceData(
            SummonerAccount Summoner,
            Rank SoloRank,
            int Wins,
            int Losses,
            double AverageKda,
            int MatchCount,
            IReadOnlyList<bool> RecentResults,
            IReadOnlyList<LiveMatchRecentMatchSnapshot> RecentMatches);

        private readonly record struct PlayerVisuals(
            string ChampionIcon,
            string Spell1Icon,
            string Spell2Icon);

        private sealed class RosterDefinition
        {
            public RosterDefinition(long gameId, string signature,
                IReadOnlyList<LiveMatchPlayerSnapshot> myTeam,
                IReadOnlyList<LiveMatchPlayerSnapshot> theirTeam)
            {
                GameId = gameId;
                Signature = signature;
                MyTeam = myTeam;
                TheirTeam = theirTeam;
            }

            public long GameId { get; }

            public string Signature { get; }

            public IReadOnlyList<LiveMatchPlayerSnapshot> MyTeam { get; }

            public IReadOnlyList<LiveMatchPlayerSnapshot> TheirTeam { get; }
        }

        private sealed class FetchResult<T> where T : class
        {
            private FetchResult(T value, bool failed, string error, Exception exception)
            {
                Value = value;
                Failed = failed;
                Error = error;
                Exception = exception;
            }

            public T Value { get; }

            public bool Failed { get; }

            public string Error { get; }

            public Exception Exception { get; }

            public static FetchResult<T> Success(T value) => new(value, false, null, null);

            public static FetchResult<T> Unavailable() => new(null, false, null, null);

            public static FetchResult<T> Failure(string error, Exception exception) =>
                new(null, true, error, exception);
        }

        private sealed record SnapshotErrorLogContext(
            string SafeMessage,
            IReadOnlyList<Exception> Exceptions);
    }
}

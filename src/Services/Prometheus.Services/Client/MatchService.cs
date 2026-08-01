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
        private static readonly JsonSerializerSettings SnapshotCloneSettings = new()
        {
            ContractResolver = new WritablePropertiesContractResolver()
        };

        private readonly ILeagueClient _leagueClient;
        private readonly IHttpService _httpService;
        private readonly IGameService _gameService;
        private readonly ISummonerService _summonerService;
        private readonly IGameResourceManager _gameResourceManager;
        private readonly object _publicationSync = new();
        private readonly object _snapshotSync = new();
        private readonly object _stateSync = new();
        private readonly object _rosterSync = new();
        private readonly SemaphoreSlim _lifecycleGate = new(1, 1);
        private readonly SemaphoreSlim _connectionGate = new(1, 1);
        private readonly SemaphoreSlim _refreshGate = new(1, 1);
        private readonly SemaphoreSlim _playerLoadGate =
            new(MaximumConcurrentPlayerLoads, MaximumConcurrentPlayerLoads);
        private readonly ConcurrentDictionary<PlayerCacheKey,
            Lazy<Task<PlayerPerformanceData>>> _playerCache = new();

        private LiveMatchSnapshot _current = LiveMatchSnapshot.Empty;
        private CancellationTokenSource _lifetimeCts;
        private CancellationTokenSource _phaseCts;
        private CancellationTokenSource _acceptAutomationCts;
        private CancellationTokenSource _reconnectAutomationCts;
        private CancellationTokenSource _aramBenchSwapAutomationCts;
        private Task _acceptAutomationTask = Task.CompletedTask;
        private Task _reconnectAutomationTask = Task.CompletedTask;
        private Task _aramBenchSwapAutomationTask = Task.CompletedTask;
        private bool _started;
        private bool _subscribed;
        private long _phaseVersion;
        private long _phaseInstance;
        private long _lastAutoAcceptInstance = -1;
        private long _lastAutoReconnectInstance = -1;
        private string _lastAramBenchSwapState = string.Empty;
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
        {
            _leagueClient = leagueClient ?? throw new ArgumentNullException(nameof(leagueClient));
            _httpService = httpService ?? throw new ArgumentNullException(nameof(httpService));
            _gameService = gameService ?? throw new ArgumentNullException(nameof(gameService));
            _summonerService = summonerService ??
                throw new ArgumentNullException(nameof(summonerService));
            _gameResourceManager = gameResourceManager ??
                throw new ArgumentNullException(nameof(gameResourceManager));
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

            var json = JsonConvert.SerializeObject(source, SnapshotCloneSettings);
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
                _phaseVersion++;
                _phaseInstance++;
                _initializedConnection = string.Empty;
                _lastAramBenchSwapState = string.Empty;
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

            // RefreshAsync is the user-visible/manual refresh boundary. Any
            // in-flight roster enrichment belongs to the previous request,
            // and only successful data may survive in the cache between
            // automatic websocket updates.
            _ = CancelRosterEnrichment(clearCache: true, resetSignature: true);

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
                : TransitionPhase(rawPhase);

            await RefreshForPhaseAsync(context, phaseError, cancellationToken)
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
            CancellationTokenSource acceptCts;
            CancellationTokenSource reconnectCts;
            CancellationTokenSource aramBenchSwapCts;
            Task acceptTask;
            Task reconnectTask;
            Task aramBenchSwapTask;

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
                    acceptCts = _acceptAutomationCts;
                    reconnectCts = _reconnectAutomationCts;
                    aramBenchSwapCts = _aramBenchSwapAutomationCts;
                    acceptTask = _acceptAutomationTask;
                    reconnectTask = _reconnectAutomationTask;
                    aramBenchSwapTask = _aramBenchSwapAutomationTask;
                    _lifetimeCts = null;
                    _phaseCts = null;
                    _acceptAutomationCts = null;
                    _reconnectAutomationCts = null;
                    _aramBenchSwapAutomationCts = null;
                    _lastAramBenchSwapState = string.Empty;
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
            acceptCts?.Cancel();
            reconnectCts?.Cancel();
            aramBenchSwapCts?.Cancel();
            var rosterTask = CancelRosterEnrichment(clearCache: true, resetSignature: true);
            _currentSummoner = null;
            _httpService.Reset();

            await _leagueClient.StopAsync(cancellationToken).ConfigureAwait(false);
            await AwaitAutomationTaskAsync(acceptTask).ConfigureAwait(false);
            await AwaitAutomationTaskAsync(reconnectTask).ConfigureAwait(false);
            await AwaitAutomationTaskAsync(aramBenchSwapTask).ConfigureAwait(false);
            await AwaitAutomationTaskAsync(rosterTask).ConfigureAwait(false);

            lifetimeCts?.Dispose();
            phaseCts?.Dispose();
            acceptCts?.Dispose();
            reconnectCts?.Dispose();
            aramBenchSwapCts?.Dispose();

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
            }

            phaseCts?.Cancel();
            acceptCts?.Cancel();
            reconnectCts?.Cancel();
            aramBenchSwapCts?.Cancel();
            CancelRosterEnrichment(clearCache: true, resetSignature: true);
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

            var context = TransitionPhase(rawPhase);
            _ = RefreshForPhaseSafelyAsync(context);
        }

        private async Task RefreshForPhaseSafelyAsync(PhaseContext context)
        {
            try
            {
                await RefreshForPhaseAsync(context, null, CancellationToken.None).ConfigureAwait(false);
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
                var context = TransitionPhase(value.Phase);
                _ = RefreshForPhaseSafelyAsync(context);
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
        }

        private void HandleMatchmakingEvent(OnWebsocketEventArgs args)
        {
            var value = IsDelete(args) ? null : DeserializeEvent<MatchmakingSnapshot>(args?.Data);
            PublishResourceUpdate(snapshot =>
            {
                snapshot.Matchmaking = value;
                return snapshot;
            });
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
            CancellationToken cancellationToken)
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
                    ? FetchAsync("post-game", () => _gameService.GetPostGameSnapshotAsync(token), token)
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

                    if (IsPostGamePhase(context.Phase) && !postGame.Failed)
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
                });
                ScheduleRosterRefresh();
                TriggerAutomation(context);
            }
            finally
            {
                _refreshGate.Release();
            }
        }

        private void ScheduleRosterRefresh()
        {
            var source = GetCurrentSnapshot();
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
                if (string.Equals(_rosterSourceSignature, sourceSignature,
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
                generation, rosterCts.Token);
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
            string sourceSignature, long generation, CancellationToken cancellationToken)
        {
            try
            {
                if (!IsRosterPhase(source.GameflowPhase))
                {
                    PublishRoster(generation, cancellationToken, null);
                    return;
                }

                RosterDefinition definition;
                if (source.GameflowPhase == GameflowPhase.ChampSelect &&
                    !ShouldUseGameflowRoster(source))
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

                PrunePlayerCache(definition.GameId);
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
                Log.Error(exception, "Unable to assemble the live-match roster");
                PublishRosterFailure(generation, cancellationToken,
                    "Unable to assemble the live-match roster.");
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

                var performance = await GetPlayerPerformanceAsync(gameId, player.Puuid,
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
                MarkRosterRetryable(generation);
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

        private Task<PlayerPerformanceData> GetPlayerPerformanceAsync(long gameId,
            string puuid, CancellationToken cancellationToken)
        {
            // Some client builds briefly omit the game id during champion
            // select. Never let that sentinel value share player data across
            // separate matches.
            if (gameId <= 0)
            {
                return LoadPlayerPerformanceAsync(puuid, cancellationToken);
            }

            var key = new PlayerCacheKey(gameId, NormalizePuuid(puuid));
            var lazy = _playerCache.GetOrAdd(key, _ =>
                new Lazy<Task<PlayerPerformanceData>>(
                    () => LoadPlayerPerformanceAsync(puuid, cancellationToken),
                    LazyThreadSafetyMode.ExecutionAndPublication));
            return AwaitCachedPlayerPerformanceAsync(key, lazy);
        }

        private async Task<PlayerPerformanceData> AwaitCachedPlayerPerformanceAsync(
            PlayerCacheKey key, Lazy<Task<PlayerPerformanceData>> lazy)
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
            var matchesTask = _summonerService.GetMatchesResultAsync(
                puuid, 0, RecentMatchCount - 1, cancellationToken);

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
            var stats = matches
                .Select(match => match.Participants?.FirstOrDefault()?.Stats)
                .Where(value => value is not null)
                .ToArray();
            var wins = stats.Count(value => value.Win);
            var losses = stats.Length - wins;
            var killsAndAssists = stats.Sum(value => value.Kills + value.Assists);
            var deaths = stats.Sum(value => value.Deaths);
            var averageKda = stats.Length == 0
                ? 0d
                : killsAndAssists / (double)Math.Max(1, deaths);

            return new PlayerPerformanceData(summoner, rank, wins, losses,
                averageKda, stats.Length,
                stats.Take(5).Select(value => value.Win).ToArray());
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
            CancellationToken cancellationToken, string error)
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
            });
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

        private void PrunePlayerCache(long gameId)
        {
            foreach (var key in _playerCache.Keys.Where(key => key.GameId != gameId))
            {
                _playerCache.TryRemove(key, out _);
            }
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
            var useGameflowRoster = ShouldUseGameflowRoster(snapshot);
            var isGameflowPhase = snapshot.GameflowPhase is GameflowPhase.GameStart or
                GameflowPhase.InProgress or GameflowPhase.Reconnect;
            if (snapshot.GameflowPhase == GameflowPhase.ChampSelect &&
                !useGameflowRoster)
            {
                var championSelect = snapshot.ChampionSelect;
                builder.Append(':').Append(championSelect?.LocalPlayerCellId ?? 0);
                AppendChampionSelectSignature(builder, championSelect?.MyTeam, false);
                AppendChampionSelectSignature(builder, championSelect?.TheirTeam, true);
            }
            else if (isGameflowPhase || useGameflowRoster)
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

        private static bool ShouldUseGameflowRoster(LiveMatchSnapshot snapshot)
        {
            return snapshot?.GameflowSession?.GameClient?.Running == true &&
                HasGameflowTeams(snapshot.GameflowSession.GameData);
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

        private PhaseContext TransitionPhase(string rawPhase)
        {
            rawPhase = rawPhase?.Trim().Trim('"') ?? string.Empty;
            var parsedPhase = ParsePhase(rawPhase);
            CancellationTokenSource oldPhaseCts = null;
            PhaseContext context;

            lock (_stateSync)
            {
                if (!_started || _lifetimeCts is null)
                {
                    return new PhaseContext(_phaseVersion, _phaseInstance, parsedPhase,
                        rawPhase, new CancellationToken(true));
                }

                if (string.Equals(GetCurrentSnapshot().RawPhase, rawPhase,
                        StringComparison.OrdinalIgnoreCase) &&
                    _phaseCts is not null)
                {
                    return new PhaseContext(_phaseVersion, _phaseInstance, parsedPhase,
                        rawPhase, _phaseCts.Token);
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

            PublishSnapshot(snapshot => PrepareForPhase(snapshot, parsedPhase, rawPhase));
            ScheduleRosterRefresh();
            TriggerAutomation(context);
            return context;
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
                StartAutoReconnect(context);
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
                        StringComparison.Ordinal))
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
            var gameData = snapshot?.GameflowSession?.GameData;
            if (gameData is not null &&
                (gameData.QueueId == 450 || gameData.MapId == 12 ||
                 string.Equals(gameData.GameMode, "ARAM",
                     StringComparison.OrdinalIgnoreCase)))
            {
                return true;
            }

            var lobbyConfig = snapshot?.Lobby?.GameConfig;
            return lobbyConfig is not null &&
                   (lobbyConfig.MapId == 12 ||
                    string.Equals(lobbyConfig.GameMode, "ARAM",
                        StringComparison.OrdinalIgnoreCase));
        }

        private static string BuildAramBenchSwapState(
            PhaseContext context,
            LiveMatchSnapshot snapshot,
            IReadOnlyList<int> preferredChampionIds)
        {
            var championSelect = snapshot?.ChampionSelect;
            var gameData = snapshot?.GameflowSession?.GameData;
            var localChampionId = championSelect?.MyTeam?.FirstOrDefault(member =>
                    member?.CellId == championSelect.LocalPlayerCellId)?.ChampionId ?? 0;
            var builder = new StringBuilder()
                .Append(context.Instance).Append('|')
                .Append(gameData?.QueueId ?? 0).Append('|')
                .Append(gameData?.MapId ?? 0).Append('|')
                .Append(gameData?.GameMode).Append('|')
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
            CancellationToken token;
            lock (_stateSync)
            {
                if (!_started || _lastAutoReconnectInstance == context.Instance ||
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
                    ReconnectRetryDelays, _gameService.ReconnectGameAsync, token);
            }
        }

        private async Task RunAutomationAsync(string operationName, PhaseContext context,
            IReadOnlyList<TimeSpan> retryDelays, Func<CancellationToken, Task> operation,
            CancellationToken cancellationToken)
        {
            Exception lastError = null;
            for (var attempt = 0; attempt < retryDelays.Count; attempt++)
            {
                if (retryDelays[attempt] > TimeSpan.Zero)
                {
                    await Task.Delay(retryDelays[attempt], cancellationToken).ConfigureAwait(false);
                }

                if (!IsCurrentPhase(context))
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
            });
        }

        private void PublishSnapshot(Func<LiveMatchSnapshot, LiveMatchSnapshot> update)
        {
            lock (_publicationSync)
            {
                LiveMatchSnapshot next;
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
                    _current = next;
                }

                // Keep version assignment and observer notification in one
                // serialized publication lane. Consumers therefore never see
                // a newer version before an older one from another thread.
                var handlers = SnapshotChanged;
                if (handlers is null)
                {
                    return;
                }

                foreach (EventHandler<LiveMatchSnapshotChangedEventArgs> handler in
                         handlers.GetInvocationList())
                {
                    try
                    {
                        var args = new LiveMatchSnapshotChangedEventArgs(
                            CloneForConsumer(next));
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
                Error = source.Error
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
                    next.PostGame = null;
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
                return FetchResult<T>.Failure(FormatError(name, exception));
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

        private readonly record struct PlayerCacheKey(long GameId, string Puuid);

        private sealed record PlayerPerformanceData(
            SummonerAccount Summoner,
            Rank SoloRank,
            int Wins,
            int Losses,
            double AverageKda,
            int MatchCount,
            IReadOnlyList<bool> RecentResults);

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
            private FetchResult(T value, bool failed, string error)
            {
                Value = value;
                Failed = failed;
                Error = error;
            }

            public T Value { get; }

            public bool Failed { get; }

            public string Error { get; }

            public static FetchResult<T> Success(T value) => new(value, false, null);

            public static FetchResult<T> Unavailable() => new(null, false, null);

            public static FetchResult<T> Failure(string error) => new(null, true, error);
        }
    }
}

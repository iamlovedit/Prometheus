using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Prometheus.Core.Models;
using Prometheus.Services.Interfaces;
using Prometheus.Services.Interfaces.Client;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Prometheus.Services.Client
{
    /// <summary>
    /// Coordinates the LCU websocket lifecycle with cancellation-aware HTTP
    /// snapshots.  Every published snapshot is a replacement instance, making
    /// Current safe for UI readers without holding the service's locks.
    /// </summary>
    public class MatchService : IMatchService
    {
        private const string PhaseUri = "/lol-gameflow/v1/gameflow-phase";
        private const string SessionUri = "/lol-gameflow/v1/session";
        private const string LobbyUri = "/lol-lobby/v2/lobby";
        private const string MatchmakingUri = "/lol-matchmaking/v1/search";
        private const string ReadyCheckUri = "/lol-matchmaking/v1/ready-check";
        private const string ChampionSelectUri = "/lol-champ-select/v1/session";

        private static readonly TimeSpan[] AcceptRetryDelays =
            [TimeSpan.Zero, TimeSpan.FromMilliseconds(500), TimeSpan.FromMilliseconds(1500)];

        private static readonly TimeSpan[] ReconnectRetryDelays =
            [TimeSpan.Zero, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(5)];

        private readonly ILeagueClient _leagueClient;
        private readonly IHttpService _httpService;
        private readonly IGameService _gameService;
        private readonly object _snapshotSync = new();
        private readonly object _stateSync = new();
        private readonly SemaphoreSlim _lifecycleGate = new(1, 1);
        private readonly SemaphoreSlim _connectionGate = new(1, 1);
        private readonly SemaphoreSlim _refreshGate = new(1, 1);

        private LiveMatchSnapshot _current = LiveMatchSnapshot.Empty;
        private CancellationTokenSource _lifetimeCts;
        private CancellationTokenSource _phaseCts;
        private CancellationTokenSource _acceptAutomationCts;
        private CancellationTokenSource _reconnectAutomationCts;
        private Task _acceptAutomationTask = Task.CompletedTask;
        private Task _reconnectAutomationTask = Task.CompletedTask;
        private bool _started;
        private bool _subscribed;
        private long _phaseVersion;
        private long _phaseInstance;
        private long _lastAutoAcceptInstance = -1;
        private long _lastAutoReconnectInstance = -1;
        private string _initializedConnection = string.Empty;

        public MatchService(ILeagueClient leagueClient, IHttpService httpService, IGameService gameService,
            IGameAutomationSettings automationSettings = null)
        {
            _leagueClient = leagueClient ?? throw new ArgumentNullException(nameof(leagueClient));
            _httpService = httpService ?? throw new ArgumentNullException(nameof(httpService));
            _gameService = gameService ?? throw new ArgumentNullException(nameof(gameService));
            AutomationSettings = automationSettings ?? GameAutomationSettings.Default;
        }

        public LiveMatchSnapshot Current
        {
            get
            {
                lock (_snapshotSync)
                {
                    return _current;
                }
            }
        }

        public IGameAutomationSettings AutomationSettings { get; }

        public event EventHandler<LiveMatchSnapshotChangedEventArgs> SnapshotChanged;

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
                AttachSubscriptions();
                AutomationSettings.PropertyChanged += HandleAutomationSettingsChanged;
                lifetimeToken = _lifetimeCts.Token;
            }
            finally
            {
                _lifecycleGate.Release();
            }

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
            Task acceptTask;
            Task reconnectTask;

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
                    acceptTask = _acceptAutomationTask;
                    reconnectTask = _reconnectAutomationTask;
                    _lifetimeCts = null;
                    _phaseCts = null;
                    _acceptAutomationCts = null;
                    _reconnectAutomationCts = null;
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

            await _leagueClient.StopAsync(cancellationToken).ConfigureAwait(false);
            await AwaitAutomationTaskAsync(acceptTask).ConfigureAwait(false);
            await AwaitAutomationTaskAsync(reconnectTask).ConfigureAwait(false);

            lifetimeCts?.Dispose();
            phaseCts?.Dispose();
            acceptCts?.Dispose();
            reconnectCts?.Dispose();

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
            _leagueClient.Subscribe(PhaseUri, HandlePhaseEvent);
            _leagueClient.Subscribe(SessionUri, HandleSessionEvent);
            _leagueClient.Subscribe(LobbyUri, HandleLobbyEvent);
            _leagueClient.Subscribe(MatchmakingUri, HandleMatchmakingEvent);
            _leagueClient.Subscribe(ReadyCheckUri, HandleReadyCheckEvent);
            _leagueClient.Subscribe(ChampionSelectUri, HandleChampionSelectEvent);
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
            _leagueClient.Unsubscribe(PhaseUri, HandlePhaseEvent);
            _leagueClient.Unsubscribe(SessionUri, HandleSessionEvent);
            _leagueClient.Unsubscribe(LobbyUri, HandleLobbyEvent);
            _leagueClient.Unsubscribe(MatchmakingUri, HandleMatchmakingEvent);
            _leagueClient.Unsubscribe(ReadyCheckUri, HandleReadyCheckEvent);
            _leagueClient.Unsubscribe(ChampionSelectUri, HandleChampionSelectEvent);
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
                    Current.ConnectionState == ConnectionState.Connected)
                {
                    return;
                }

                // Connected is not published until this succeeds.
                _httpService.Initialize(port, _leagueClient.Token);
                _initializedConnection = connectionId;

                PublishSnapshot(snapshot =>
                {
                    var next = CopySnapshot(snapshot);
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
            lock (_stateSync)
            {
                if (!_started)
                {
                    return;
                }

                phaseCts = _phaseCts;
                acceptCts = _acceptAutomationCts;
                reconnectCts = _reconnectAutomationCts;
                _phaseCts = CancellationTokenSource.CreateLinkedTokenSource(_lifetimeCts.Token);
                _phaseVersion++;
                _phaseInstance++;
                _initializedConnection = string.Empty;
            }

            phaseCts?.Cancel();
            acceptCts?.Cancel();
            reconnectCts?.Cancel();

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
                !string.Equals(value.Phase, Current.RawPhase, StringComparison.OrdinalIgnoreCase))
            {
                var context = TransitionPhase(value.Phase);
                _ = RefreshForPhaseSafelyAsync(context);
            }
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
                if (!IsCurrentPhase(context))
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

                if (!IsCurrentPhase(context))
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
                    return next;
                });
            }
            finally
            {
                _refreshGate.Release();
            }
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

                if (string.Equals(Current.RawPhase, rawPhase, StringComparison.OrdinalIgnoreCase) &&
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
            TriggerAutomation(context);
            return context;
        }

        private PhaseContext GetCurrentPhaseContext()
        {
            lock (_stateSync)
            {
                var current = Current;
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
            LiveMatchSnapshot next;
            lock (_snapshotSync)
            {
                next = update(_current) ?? _current;
                next.UpdatedAt = DateTimeOffset.UtcNow;
                _current = next;
            }

            var handlers = SnapshotChanged;
            if (handlers is null)
            {
                return;
            }

            var args = new LiveMatchSnapshotChangedEventArgs(next);
            foreach (EventHandler<LiveMatchSnapshotChangedEventArgs> handler in handlers.GetInvocationList())
            {
                try
                {
                    handler(this, args);
                }
                catch
                {
                    // Snapshot publication is not allowed to break the
                    // websocket or retry loop because one observer failed.
                }
            }
        }

        private static LiveMatchSnapshot CopySnapshot(LiveMatchSnapshot source)
        {
            return new LiveMatchSnapshot
            {
                ConnectionState = source.ConnectionState,
                GameflowPhase = source.GameflowPhase,
                RawPhase = source.RawPhase,
                GameflowSession = source.GameflowSession,
                Lobby = source.Lobby,
                Matchmaking = source.Matchmaking,
                ReadyCheck = source.ReadyCheck,
                ChampionSelect = source.ChampionSelect,
                PostGame = source.PostGame,
                UpdatedAt = source.UpdatedAt,
                DataQuality = source.DataQuality,
                Error = source.Error,
                Errors = source.Errors ?? Array.Empty<string>()
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

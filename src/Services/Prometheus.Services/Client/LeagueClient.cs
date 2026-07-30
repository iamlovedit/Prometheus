using Newtonsoft.Json.Linq;
using Prometheus.Core.Models;
using Prometheus.Services.Interfaces.Client;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Authentication;
using System.Threading;
using System.Threading.Tasks;
using WebSocketSharp;

namespace Prometheus.Services.Client
{
    /// <summary>
    /// Owns one restartable websocket lifecycle.  A single retry loop survives
    /// unexpected disconnects and is cancelled deterministically by StopAsync.
    /// </summary>
    public class LeagueClient : ILeagueClient
    {
        private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(2);

        private readonly IClientService _clientService;
        private readonly object _stateSync = new();
        private readonly object _subscriptionsSync = new();
        private readonly SemaphoreSlim _lifecycleGate = new(1, 1);
        private readonly Dictionary<string, List<Action<OnWebsocketEventArgs>>> _eventsMap = [];

        private WebSocket _socketConnection;
        private CancellationTokenSource _lifetimeCts;
        private Task _retryLoop;
        private TaskCompletionSource<bool> _firstAttempt;
        private TaskCompletionSource<bool> _disconnectedSignal = CreateSignal();
        private bool _connected;
        private bool _stopping;

        public LeagueClient(IClientService clientService)
        {
            _clientService = clientService ?? throw new ArgumentNullException(nameof(clientService));
        }

        public event Action OnConnected;

        public event Action OnDisconnected;

        public event Action<OnWebsocketEventArgs> OnWebsocketEvent;

        public bool Connected
        {
            get
            {
                lock (_stateSync)
                {
                    return _connected;
                }
            }
        }

        public string Port { get; set; } = string.Empty;

        public string Token { get; set; } = string.Empty;

        public int ProcessId { get; set; }

        public void Subscribe(string uri, Action<OnWebsocketEventArgs> args)
        {
            if (string.IsNullOrWhiteSpace(uri))
            {
                throw new ArgumentException("A subscription URI is required.", nameof(uri));
            }

            if (args is null)
            {
                throw new ArgumentNullException(nameof(args));
            }

            lock (_subscriptionsSync)
            {
                if (!_eventsMap.TryGetValue(uri, out var events))
                {
                    events = [];
                    _eventsMap.Add(uri, events);
                }

                if (!events.Contains(args))
                {
                    events.Add(args);
                }
            }
        }

        public void Unsubscribe(string uri, Action<OnWebsocketEventArgs> action)
        {
            if (string.IsNullOrWhiteSpace(uri) || action is null)
            {
                return;
            }

            lock (_subscriptionsSync)
            {
                if (!_eventsMap.TryGetValue(uri, out var events))
                {
                    return;
                }

                events.RemoveAll(item => item == action);
                if (events.Count == 0)
                {
                    _eventsMap.Remove(uri);
                }
            }
        }

        public Task<bool> StartAsync()
        {
            return StartAsync(CancellationToken.None);
        }

        public async Task<bool> StartAsync(CancellationToken cancellationToken)
        {
            Task<bool> firstAttempt;
            await _lifecycleGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                if (Connected)
                {
                    return true;
                }

                if (_retryLoop is null || _retryLoop.IsCompleted)
                {
                    _stopping = false;
                    _lifetimeCts?.Dispose();
                    _lifetimeCts = new CancellationTokenSource();
                    _firstAttempt = CreateSignal();
                    _retryLoop = RunConnectionLoopAsync(_lifetimeCts.Token);
                }

                firstAttempt = _firstAttempt.Task;
            }
            finally
            {
                _lifecycleGate.Release();
            }

            return await firstAttempt.WaitAsync(cancellationToken).ConfigureAwait(false);
        }

        public Task StopAsync()
        {
            return StopAsync(CancellationToken.None);
        }

        public async Task StopAsync(CancellationToken cancellationToken)
        {
            Task retryLoop;
            WebSocket socket;
            bool notifyDisconnected;

            await _lifecycleGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                lock (_stateSync)
                {
                    _stopping = true;
                    notifyDisconnected = _connected;
                    _connected = false;
                    socket = _socketConnection;
                    _socketConnection = null;
                    _disconnectedSignal.TrySetResult(true);
                }

                _lifetimeCts?.Cancel();
                _firstAttempt?.TrySetResult(false);
                retryLoop = _retryLoop;
            }
            finally
            {
                _lifecycleGate.Release();
            }

            if (socket is not null)
            {
                DetachSocket(socket);
                try
                {
                    if (socket.IsAlive)
                    {
                        socket.Close(CloseStatusCode.Normal);
                    }
                }
                catch (WebSocketException)
                {
                }
            }

            if (retryLoop is not null)
            {
                try
                {
                    await retryLoop.WaitAsync(cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (_lifetimeCts?.IsCancellationRequested == true
                                                        && !cancellationToken.IsCancellationRequested)
                {
                }
            }

            if (notifyDisconnected)
            {
                InvokeSafely(OnDisconnected);
            }
        }

        private async Task RunConnectionLoopAsync(CancellationToken cancellationToken)
        {
            var isFirstAttempt = true;
            try
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    var connected = false;
                    try
                    {
                        connected = TryConnect(cancellationToken);
                    }
                    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                    {
                        break;
                    }
                    catch (Exception)
                    {
                        connected = false;
                    }

                    if (isFirstAttempt)
                    {
                        _firstAttempt.TrySetResult(connected);
                        isFirstAttempt = false;
                    }

                    if (connected)
                    {
                        Task disconnectedTask;
                        lock (_stateSync)
                        {
                            disconnectedTask = _disconnectedSignal.Task;
                        }

                        await disconnectedTask.WaitAsync(cancellationToken).ConfigureAwait(false);
                    }

                    if (!cancellationToken.IsCancellationRequested)
                    {
                        await Task.Delay(RetryDelay, cancellationToken).ConfigureAwait(false);
                    }
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
            }
            finally
            {
                _firstAttempt?.TrySetResult(false);
            }
        }

        private bool TryConnect(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (Connected)
            {
                return true;
            }

            var processId = _clientService.GetClientProcessId();
            var argsMap = _clientService.GetClientCommandLines();
            if (processId <= 0 || argsMap is null ||
                !argsMap.TryGetValue("--app-port", out var port) ||
                !argsMap.TryGetValue("--remoting-auth-token", out var token) ||
                string.IsNullOrWhiteSpace(port) || string.IsNullOrWhiteSpace(token))
            {
                return false;
            }

            var socket = new WebSocket($"wss://127.0.0.1:{port}/", "wamp");
            socket.SetCredentials("riot", token, true);
            socket.SslConfiguration.EnabledSslProtocols = SslProtocols.Tls12;
            socket.SslConfiguration.ServerCertificateValidationCallback = (a, b, c, d) => true;
            socket.OnMessage += HandleMessageReceived;
            socket.OnClose += HandleDisconnected;

            lock (_stateSync)
            {
                if (_stopping || cancellationToken.IsCancellationRequested)
                {
                    DetachSocket(socket);
                    return false;
                }

                _socketConnection = socket;
                _disconnectedSignal = CreateSignal();
            }

            try
            {
                socket.Connect();
                cancellationToken.ThrowIfCancellationRequested();
                if (!socket.IsAlive)
                {
                    CleanupFailedSocket(socket);
                    return false;
                }

                socket.Send("[5, \"OnJsonApiEvent\"]");

                lock (_stateSync)
                {
                    if (_stopping || cancellationToken.IsCancellationRequested ||
                        !ReferenceEquals(_socketConnection, socket))
                    {
                        CleanupFailedSocket(socket);
                        return false;
                    }

                    ProcessId = processId;
                    Port = port;
                    Token = token;
                    _connected = true;
                }

                InvokeSafely(OnConnected);
                return true;
            }
            catch
            {
                CleanupFailedSocket(socket);
                throw;
            }
        }

        private void CleanupFailedSocket(WebSocket socket)
        {
            bool clear;
            lock (_stateSync)
            {
                clear = ReferenceEquals(_socketConnection, socket);
                if (clear)
                {
                    _socketConnection = null;
                    _connected = false;
                    _disconnectedSignal.TrySetResult(true);
                }
            }

            DetachSocket(socket);
            try
            {
                if (socket.IsAlive)
                {
                    socket.Close(CloseStatusCode.Normal);
                }
            }
            catch (WebSocketException)
            {
            }
        }

        private void HandleMessageReceived(object sender, MessageEventArgs args)
        {
            if (!args.IsText)
            {
                return;
            }

            OnWebsocketEventArgs eventArgs;
            try
            {
                var payload = JArray.Parse(args.Data);
                if (payload.Count != 3 || payload[0].ToObject<byte>() != 8 ||
                    payload[1].ToObject<string>() != "OnJsonApiEvent")
                {
                    return;
                }

                eventArgs = payload[2].ToObject<OnWebsocketEventArgs>();
                if (eventArgs is null || string.IsNullOrWhiteSpace(eventArgs.Uri))
                {
                    return;
                }
            }
            catch
            {
                return;
            }

            InvokeSafely(OnWebsocketEvent, eventArgs);

            Action<OnWebsocketEventArgs>[] subscribers;
            lock (_subscriptionsSync)
            {
                subscribers = _eventsMap.TryGetValue(eventArgs.Uri, out var events)
                    ? events.ToArray()
                    : Array.Empty<Action<OnWebsocketEventArgs>>();
            }

            foreach (var subscriber in subscribers)
            {
                try
                {
                    subscriber(eventArgs);
                }
                catch
                {
                    // One observer must not prevent the remaining observers
                    // from receiving the LCU event.
                }
            }
        }

        private void HandleDisconnected(object sender, CloseEventArgs args)
        {
            var socket = sender as WebSocket;
            var notify = false;
            lock (_stateSync)
            {
                if (socket is not null && !ReferenceEquals(socket, _socketConnection))
                {
                    return;
                }

                notify = _connected && !_stopping;
                _connected = false;
                _socketConnection = null;
                _disconnectedSignal.TrySetResult(true);
            }

            if (socket is not null)
            {
                DetachSocket(socket);
            }

            if (notify)
            {
                InvokeSafely(OnDisconnected);
            }
        }

        private void DetachSocket(WebSocket socket)
        {
            socket.OnMessage -= HandleMessageReceived;
            socket.OnClose -= HandleDisconnected;
        }

        private static TaskCompletionSource<bool> CreateSignal()
        {
            return new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        private static void InvokeSafely(Action handlers)
        {
            if (handlers is null)
            {
                return;
            }

            foreach (Action handler in handlers.GetInvocationList())
            {
                try
                {
                    handler();
                }
                catch
                {
                }
            }
        }

        private static void InvokeSafely<T>(Action<T> handlers, T value)
        {
            if (handlers is null)
            {
                return;
            }

            foreach (Action<T> handler in handlers.GetInvocationList())
            {
                try
                {
                    handler(value);
                }
                catch
                {
                }
            }
        }
    }
}

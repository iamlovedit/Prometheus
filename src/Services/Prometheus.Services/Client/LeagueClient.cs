using Newtonsoft.Json.Linq;
using Prometheus.Core.Models;
using Prometheus.Services.Interfaces.Client;
using Serilog;
using System.Net.Security;
using System.Net.WebSockets;
using System.Text;

namespace Prometheus.Services.Client
{
    /// <summary>
    /// Owns one restartable LCU websocket lifecycle. The receive loop uses the
    /// framework ClientWebSocket implementation so every receive continuation is
    /// asynchronous and cannot recursively consume the native TLS thread stack.
    /// </summary>
    public class LeagueClient : ILeagueClient
    {
        private const int ReceiveBufferSize = 16 * 1024;
        private const int MaximumMessageSize = 16 * 1024 * 1024;

        private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(2);
        private static readonly TimeSpan CloseTimeout = TimeSpan.FromSeconds(1);

        private readonly IClientService _clientService;
        private readonly ILogger _logger;
        private readonly object _stateSync = new();
        private readonly object _connectionTransitionSync = new();
        private readonly object _subscriptionsSync = new();
        private readonly SemaphoreSlim _lifecycleGate = new(1, 1);
        private readonly Dictionary<string, List<Action<OnWebsocketEventArgs>>> _eventsMap = [];

        private ClientWebSocket _socketConnection;
        private CancellationTokenSource _lifetimeCts;
        private Task _retryLoop;
        private TaskCompletionSource<bool> _firstAttempt;
        private bool _connected;
        private bool _stopping;

        public LeagueClient(IClientService clientService)
            : this(clientService, Log.Logger)
        {
        }

        internal LeagueClient(IClientService clientService, ILogger logger)
        {
            _clientService = clientService ?? throw new ArgumentNullException(nameof(clientService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
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
            await _lifecycleGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                Task retryLoop;
                ClientWebSocket socket;
                CancellationTokenSource lifetimeCts;
                bool notifyDisconnected;

                lock (_connectionTransitionSync)
                {
                    lock (_stateSync)
                    {
                        _stopping = true;
                        notifyDisconnected = _connected;
                        _connected = false;
                        socket = _socketConnection;
                        _socketConnection = null;
                    }
                }

                lifetimeCts = _lifetimeCts;
                lifetimeCts?.Cancel();
                _firstAttempt?.TrySetResult(false);
                retryLoop = _retryLoop;

                await CloseSocketAsync(socket, CancellationToken.None).ConfigureAwait(false);

                if (retryLoop is not null)
                {
                    try
                    {
                        await retryLoop.ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) when (lifetimeCts?.IsCancellationRequested == true)
                    {
                    }
                    catch (Exception exception)
                    {
                        Log.Warning(exception,
                            "The League client websocket loop failed while stopping");
                    }
                }

                _retryLoop = null;
                _firstAttempt = null;
                _lifetimeCts = null;
                lifetimeCts?.Dispose();

                if (notifyDisconnected)
                {
                    InvokeSafely(OnDisconnected);
                }
            }
            finally
            {
                _lifecycleGate.Release();
            }
        }

        private async Task RunConnectionLoopAsync(CancellationToken cancellationToken)
        {
            var isFirstAttempt = true;
            try
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    ClientWebSocket socket = null;
                    try
                    {
                        socket = await TryConnectAsync(cancellationToken).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                    {
                        break;
                    }
                    catch (Exception exception)
                    {
                        Log.Debug(exception, "Unable to connect to the League client websocket");
                    }

                    if (isFirstAttempt)
                    {
                        _firstAttempt.TrySetResult(socket is not null);
                        isFirstAttempt = false;
                    }

                    if (socket is not null)
                    {
                        await ReceiveLoopSafelyAsync(socket, cancellationToken).ConfigureAwait(false);
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
            catch (Exception exception)
            {
                Log.Warning(exception, "The League client websocket loop stopped unexpectedly");
            }
            finally
            {
                _firstAttempt?.TrySetResult(false);
            }
        }

        private async Task<ClientWebSocket> TryConnectAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            lock (_stateSync)
            {
                if (_connected)
                {
                    return _socketConnection;
                }
            }

            var processId = _clientService.GetClientProcessId();
            var argsMap = _clientService.GetClientCommandLines();
            if (processId <= 0 || argsMap is null ||
                !argsMap.TryGetValue("--app-port", out var port) ||
                !argsMap.TryGetValue("--remoting-auth-token", out var token) ||
                string.IsNullOrWhiteSpace(port) || string.IsNullOrWhiteSpace(token))
            {
                return null;
            }

            var uri = new Uri($"wss://127.0.0.1:{port}/", UriKind.Absolute);
            var socket = CreateSocket(uri, token);

            lock (_stateSync)
            {
                if (_stopping || cancellationToken.IsCancellationRequested)
                {
                    socket.Dispose();
                    return null;
                }

                _socketConnection = socket;
            }

            try
            {
                await socket.ConnectAsync(uri, cancellationToken).ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
                if (socket.State != WebSocketState.Open)
                {
                    CleanupFailedSocket(socket);
                    return null;
                }

                await SendTextAsync(socket, "[5, \"OnJsonApiEvent\"]", cancellationToken)
                    .ConfigureAwait(false);

                var accepted = false;
                lock (_connectionTransitionSync)
                {
                    lock (_stateSync)
                    {
                        if (!_stopping && !cancellationToken.IsCancellationRequested &&
                            ReferenceEquals(_socketConnection, socket))
                        {
                            ProcessId = processId;
                            Port = port;
                            Token = token;
                            _connected = true;
                            accepted = true;
                        }
                    }

                    if (accepted)
                    {
                        InvokeSafely(OnConnected);
                    }
                }

                if (!accepted)
                {
                    CleanupFailedSocket(socket);
                    return null;
                }

                return socket;
            }
            catch
            {
                CleanupFailedSocket(socket);
                throw;
            }
        }

        private static ClientWebSocket CreateSocket(Uri uri, string token)
        {
            var socket = new ClientWebSocket();
            socket.Options.AddSubProtocol("wamp");
            socket.Options.KeepAliveInterval = TimeSpan.FromSeconds(5);
            var credentials = Convert.ToBase64String(Encoding.ASCII.GetBytes($"riot:{token}"));
            socket.Options.SetRequestHeader("Authorization", $"Basic {credentials}");
            socket.Options.RemoteCertificateValidationCallback = (_, _, _, errors) =>
                uri.IsLoopback || errors == SslPolicyErrors.None;
            return socket;
        }

        private async Task ReceiveLoopSafelyAsync(ClientWebSocket socket,
            CancellationToken cancellationToken)
        {
            try
            {
                await ReceiveLoopAsync(socket, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
            }
            catch (WebSocketException exception)
            {
                Log.Debug(exception, "League client websocket disconnected");
            }
            catch (IOException exception)
            {
                Log.Debug(exception, "League client TLS stream disconnected");
            }
            catch (Exception exception)
            {
                Log.Warning(exception, "Unexpected League client websocket receive failure");
            }
            finally
            {
                HandleDisconnected(socket);
            }
        }

        private async Task ReceiveLoopAsync(ClientWebSocket socket,
            CancellationToken cancellationToken)
        {
            var buffer = new byte[ReceiveBufferSize];
            while (!cancellationToken.IsCancellationRequested &&
                   socket.State is WebSocketState.Open or WebSocketState.CloseReceived)
            {
                using var message = new MemoryStream();
                long receivedLength = 0;
                var discardMessage = false;
                WebSocketReceiveResult result;
                do
                {
                    result = await socket.ReceiveAsync(
                            new ArraySegment<byte>(buffer), cancellationToken)
                        .ConfigureAwait(false);

                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        return;
                    }

                    if (result.MessageType == WebSocketMessageType.Text && result.Count > 0)
                    {
                        receivedLength += result.Count;
                        if (!discardMessage && receivedLength <= MaximumMessageSize)
                        {
                            message.Write(buffer, 0, result.Count);
                        }
                        else
                        {
                            discardMessage = true;
                        }
                    }
                }
                while (!result.EndOfMessage);

                if (discardMessage)
                {
                    Log.Warning(
                        "Ignored an oversized League client websocket message ({MessageSize} bytes; limit {MessageSizeLimit} bytes)",
                        receivedLength, MaximumMessageSize);
                    continue;
                }

                if (result.MessageType == WebSocketMessageType.Text)
                {
                    HandleMessageReceived(Encoding.UTF8.GetString(message.GetBuffer(),
                        0, checked((int)message.Length)));
                }
            }
        }

        private void CleanupFailedSocket(ClientWebSocket socket)
        {
            lock (_stateSync)
            {
                if (ReferenceEquals(_socketConnection, socket))
                {
                    _socketConnection = null;
                    _connected = false;
                }
            }

            AbortAndDispose(socket);
        }

        internal void HandleMessageReceived(string data)
        {
            OnWebsocketEventArgs eventArgs;
            try
            {
                var payload = JArray.Parse(data);
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

            LogWebsocketEvent(eventArgs);
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
                catch (Exception exception)
                {
                    Log.Warning(exception,
                        "A League client websocket subscriber failed for {Uri}", eventArgs.Uri);
                }
            }
        }

        private void LogWebsocketEvent(OnWebsocketEventArgs eventArgs)
        {
            var sanitizedData = WebsocketEventLogSanitizer.Sanitize(
                (object)eventArgs.Data,
                Token);
            var sanitizedEventType = WebsocketEventLogSanitizer.SanitizeScalar(
                eventArgs.EventType,
                Token);
            var sanitizedUri = WebsocketEventLogSanitizer.SanitizeUri(eventArgs.Uri, Token);

            var logger = _logger
                .ForContext("Kind", "Diagnostic")
                .ForContext("EventName", "lcu.websocket.event.received")
                .ForContext("Category", "WebSocket")
                .ForContext("Origin", "Observed")
                .ForContext("DataRedactedFieldCount", sanitizedData.RedactedFieldCount)
                .ForContext("DataSanitizationFailed", sanitizedData.Failed);

            if (!string.IsNullOrWhiteSpace(sanitizedData.ErrorType))
            {
                logger = logger.ForContext("DataSanitizationErrorType", sanitizedData.ErrorType);
            }

            logger.Information(
                "Received League client websocket event {EventType} for {Uri}. Data: {Data:l}",
                sanitizedEventType,
                sanitizedUri,
                sanitizedData.Data);
        }

        private void HandleDisconnected(ClientWebSocket socket)
        {
            var notify = false;
            lock (_stateSync)
            {
                if (!ReferenceEquals(socket, _socketConnection))
                {
                    return;
                }

                notify = _connected && !_stopping;
                _connected = false;
                _socketConnection = null;
            }

            AbortAndDispose(socket);
            if (notify)
            {
                InvokeSafely(OnDisconnected);
            }
        }

        private static Task SendTextAsync(ClientWebSocket socket, string message,
            CancellationToken cancellationToken)
        {
            var bytes = Encoding.UTF8.GetBytes(message);
            return socket.SendAsync(new ArraySegment<byte>(bytes),
                WebSocketMessageType.Text, true, cancellationToken);
        }

        private static async Task CloseSocketAsync(ClientWebSocket socket,
            CancellationToken cancellationToken)
        {
            if (socket is null)
            {
                return;
            }

            try
            {
                if (socket.State is WebSocketState.Open or WebSocketState.CloseReceived)
                {
                    using var closeCts = CancellationTokenSource.CreateLinkedTokenSource(
                        cancellationToken);
                    closeCts.CancelAfter(CloseTimeout);
                    await socket.CloseAsync(WebSocketCloseStatus.NormalClosure,
                            string.Empty, closeCts.Token)
                        .ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch (WebSocketException)
            {
            }
            catch (ObjectDisposedException)
            {
            }
            catch (InvalidOperationException)
            {
            }
            catch (IOException)
            {
            }
            finally
            {
                AbortAndDispose(socket);
            }
        }

        private static void AbortAndDispose(ClientWebSocket socket)
        {
            if (socket is null)
            {
                return;
            }

            try
            {
                socket.Abort();
            }
            catch (ObjectDisposedException)
            {
            }

            socket.Dispose();
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
                catch (Exception exception)
                {
                    Log.Warning(exception, "A League client lifecycle observer failed");
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
                catch (Exception exception)
                {
                    Log.Warning(exception, "A League client websocket observer failed");
                }
            }
        }
    }
}

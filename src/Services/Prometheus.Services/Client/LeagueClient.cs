using Newtonsoft.Json.Linq;
using Prometheus.Core.Models;
using Prometheus.Services.Interfaces.Client;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Security.Authentication;
using System.Threading.Tasks;
using WebSocketSharp;

namespace Prometheus.Services.Client
{
    public class LeagueClient(IClientService clientService) : ILeagueClient
    {
        private WebSocket _socketConnection;

        private bool _connected;
        public bool Connected => _connected;

        public string Port { get; set; }

        public string Token { get; set; }

        public int ProcessId { get; set; }

        public void Subscribe(string uri, Action<OnWebsocketEventArgs> args)
        {
            if (_eventsMap.TryGetValue(uri, out var events))
            {
                events.Add(args);
            }
            else
            {
                _eventsMap.Add(uri, [args]);
            }
        }

        public void Unsubscribe(string uri, Action<OnWebsocketEventArgs> action)
        {
            if (_eventsMap.TryGetValue(uri, out var events))
            {
                if (events.Count == 1)
                {
                    _eventsMap.Remove(uri);
                    return;
                }

                foreach (var item in events.ToArray())
                {
                    if (item != action)
                    {
                        continue;
                    }

                    var index = events.IndexOf(action);
                    events.RemoveAt(index);
                }
            }
        }

        public async Task StartAsync()
        {
            TryConnectOrRetry();
            await Task.Yield();
        }

        public event Action OnConnected;

        public event Action OnDisconnected;

        public event Action<OnWebsocketEventArgs> OnWebsocketEvent;

        private readonly Dictionary<string, List<Action<OnWebsocketEventArgs>>> _eventsMap = [];

        private void TryConnect()
        {
            try
            {
                if (_connected)
                {
                    return;
                }

                ProcessId = clientService.GetClientProcessId();
                var argsMap = clientService.GetClientCommandLines();
                if (argsMap is null)
                {
                    return;
                }

                if (argsMap.TryGetValue("--app-port", out var port) &&
                    argsMap.TryGetValue("--remoting-auth-token", out var token))
                {
                    Port = port;
                    Token = token;

                    _socketConnection = new WebSocket("wss://127.0.0.1:" + port + "/", "wamp");

                    _socketConnection.SetCredentials("riot", token, true);
                    _socketConnection.SslConfiguration.EnabledSslProtocols = SslProtocols.Tls12;
                    _socketConnection.SslConfiguration.ServerCertificateValidationCallback = (a, b, c, d) => true;

                    _socketConnection.OnMessage += HandleMessageReceived;
                    _socketConnection.OnClose += HandleDisConnected;
                    _socketConnection.Connect();
                    _socketConnection.Send($"[5, \"OnJsonApiEvent\"]");
                    _connected = true;

                    OnConnected?.Invoke();
                }
            }
            catch (Exception e)
            {
                _connected = false;
                if (!_connected)
                {
                    throw new InvalidOperationException(
                        $"Exception occurred trying to connect to League of Legends: {e}");
                }
            }
        }

        private void TryConnectOrRetry()
        {
            if (_connected)
            {
                return;
            }

            TryConnect();

            Task.Delay(2000).ContinueWith(a => TryConnectOrRetry());
        }

        private void HandleMessageReceived(object sender, MessageEventArgs args)
        {
            if (!args.IsText)
            {
                return;
            }

            var payload = JArray.Parse(args.Data);
            if (payload.Count != 3)
            {
                return;
            }

            if (payload[0].ToObject<byte>() != 8 || payload[1].ToObject<string>() != "OnJsonApiEvent")
            {
                return;
            }

            var eventArgs = payload[2].ToObject<OnWebsocketEventArgs>();

            OnWebsocketEvent?.Invoke(eventArgs);

            if (_eventsMap.TryGetValue(eventArgs.Uri, out var events))
            {
                foreach (var item in events)
                {
                    item.Invoke(eventArgs);
                }
            }
        }

        private void HandleDisConnected(object sender, CloseEventArgs args)
        {
            _connected = false;
            _socketConnection = null;
            OnDisconnected?.Invoke();
            TryConnectOrRetry();
        }
    }
}
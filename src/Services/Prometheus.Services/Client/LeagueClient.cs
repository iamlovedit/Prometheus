using Newtonsoft.Json.Linq;
using Prometheus.Core.Models;
using Prometheus.Services.Interfaces.Client;
using System;
using System.Collections.Generic;
using System.Security.Authentication;
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
                var argsMap = clientService.GetClientCommandLines();
                if (argsMap is null)
                {
                    return;
                }
                if (argsMap.TryGetValue("--app-port", out var port) && argsMap.TryGetValue("--remoting-auth-token", out var token))
                {
                    Port = port;
                    Token = token;

                    _socketConnection = new WebSocket("wss://127.0.0.1:" + port + "/", "wamp");

                    _socketConnection.SetCredentials("roit", token, true);
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
            catch (Exception)
            {
                _connected = false;
            }
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
        }


        internal class ClientParameter
        {
            public string Port { get; set; }

            public string Token { get; set; }
        }
    }
}

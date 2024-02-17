using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Prometheus.Services.Interfaces;
using System;
using System.Collections.Generic;
using System.Security.Authentication;
using WebSocketSharp.NetCore;

namespace Prometheus.Services
{
    public class ClientListener : IClientListener
    {
        private WebSocket _socketConnection;

        private bool _connected;

        public event Action OnConnected;

        public event Action OnDisconnected;

        public event Action<OnWebsocketEventArgs> OnWebsocketEvent;

        private readonly Dictionary<string, List<Action<OnWebsocketEventArgs>>> _eventsMap = [];

        public void Subscribe(string uri, Action<OnWebsocketEventArgs> args)
        {
            if (_eventsMap.TryGetValue(uri, out List<Action<OnWebsocketEventArgs>> value))
            {
                value.Add(args);
            }
            else
            {
                _eventsMap.Add(uri, [args]);
            }
        }

        public void TryConnect(ushort port, string token)
        {
            try
            {
                if (_connected)
                {
                    return;
                }
                _socketConnection = new WebSocket("wss://127.0.0.1:" + port.ToString() + "/", "wamp");
                _socketConnection.SetCredentials("riot", token, true);
                _socketConnection.SslConfiguration.EnabledSslProtocols = SslProtocols.Tls12;
                _socketConnection.SslConfiguration.ServerCertificateValidationCallback = (a, b, c, d) => true;
                _socketConnection.OnMessage += HandleMessage;
                _socketConnection.OnClose += HandleDisconnect;
                _socketConnection.Connect();
                _socketConnection.Send($"[5, \"OnJsonApiEvent\"]");
                _connected = true;
                OnConnected?.Invoke();
            }
            catch (Exception e)
            {
                _connected = false;
            }
        }

        public void Unsubscribe(string uri, Action<OnWebsocketEventArgs> action)
        {
            if (_eventsMap.TryGetValue(uri, out var events))
            {
                foreach (var @event in events.ToArray())
                {
                    if (@event == action)
                    {
                        var index = events.IndexOf(action);
                        events.RemoveAt(index);
                    }
                }
            }
        }

        private void HandleDisconnect(object sender, CloseEventArgs args)
        {
            _connected = false;
            _socketConnection?.Close();
            _socketConnection = null;
            OnDisconnected?.Invoke();
        }

        private void HandleMessage(object sender, MessageEventArgs args)
        {
            if (!args.IsText)
            {
                return;
            }
            var payload = JsonConvert.DeserializeObject<JArray>(args.Data);
            if (payload.Count != 3)
            {
                return;
            }
            if ((long)payload[0] != 8 || !((string)payload[1]).Equals("OnJsonApiEvent"))
            {
                return;
            }
            var @event = (dynamic)payload[2];
            OnWebsocketEvent?.Invoke(new OnWebsocketEventArgs
            {
                Path = @event["uri"],
                Type = @event["eventType"],
                Data = @event["eventType"] == "Delete" ? null : @event["data"]
            });
            if (_eventsMap.TryGetValue((string)@event["uri"], out var events))
            {
                foreach (var item in events)
                {
                    item.Invoke(new OnWebsocketEventArgs
                    {
                        Path = @event["uri"],
                        Type = @event["eventType"],
                        Data = @event["eventType"] == "Delete" ? null : @event["data"]
                    });
                }
            }
        }
    }
}

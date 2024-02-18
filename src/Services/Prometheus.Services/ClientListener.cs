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

        public bool IsConnected => _connected;

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

        public void Connect(ushort port, string token)
        {
            try
            {
                if (_connected)
                {
                    return;
                }

                _socketConnection = new WebSocket($"wss://127.0.0.1:{port}/", "wamp");
                _socketConnection.SetCredentials("riot", token, true);
                _socketConnection.SslConfiguration.EnabledSslProtocols = SslProtocols.Tls12;
                _socketConnection.SslConfiguration.ServerCertificateValidationCallback = (a, b, c, d) => true;
                _socketConnection.OnMessage += OnMessageHandler;
                _socketConnection.OnClose += OnCloseHandler;
                _socketConnection.OnOpen += OnOpenHandler;
                _socketConnection.Connect();
            }
            catch (Exception e)
            {
                _connected = false;
            }
        }

        private void OnOpenHandler(object sender, EventArgs e)
        {
            _connected = true;
            _socketConnection.Send($"[5, \"OnJsonApiEvent\"]");
            OnConnected?.Invoke();
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



        private void OnCloseHandler(object sender, CloseEventArgs args)
        {
            _connected = false;
            _socketConnection?.Close();
            _socketConnection = null;
            OnDisconnected?.Invoke();
        }

        private void OnMessageHandler(object sender, MessageEventArgs args)
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
            /*
             [8,"OnJsonApiEvent",{"data":[],"eventType":"Update","uri":"/lol-ranked/v1/notifications"}]
             */
            ;
            if (payload[0].ToObject<byte>() != 8
                || payload[1].ToObject<string>() != "OnJsonApiEvent")
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

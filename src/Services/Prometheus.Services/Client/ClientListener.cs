using Newtonsoft.Json.Linq;
using Prometheus.Services.Interfaces.Client;
using Prometheus.Services.Interfaces.Models;
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.WebSockets;
using System.Reactive.Linq;
using System.Threading.Tasks;
using Websocket.Client;

namespace Prometheus.Services.Client
{
    public class ClientListener : IClientListener
    {
        private WebsocketClient _client;

        private bool _loopAlive;

        private bool _connected;

        public event Action OnConnected;

        public event Action OnDisconnected;

        public event Action<OnWebsocketEventArgs> OnWebsocketEvent;

        private readonly Dictionary<string, List<Action<OnWebsocketEventArgs>>> _eventsMap = [];

        private bool _initialized;

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

        public void Initialize(string port, string token)
        {
            if (_initialized)
            {
                return;
            }

            _client = new WebsocketClient(new Uri($"wss://127.0.0.1:{port}/"), () =>
            {
                var clientSocket = new ClientWebSocket
                {
                    Options =
                        {
                            KeepAliveInterval = TimeSpan.FromSeconds(5),
                            Credentials = new NetworkCredential("riot", token),
                            RemoteCertificateValidationCallback =
                            (sender, cert, chain, sslPolicyErrors) => true,
                        }
                };

                clientSocket.Options.AddSubProtocol("wamp");
                clientSocket.Options.SetRequestHeader("Connection", "keep-alive");

                return clientSocket;
            });

            _client.DisconnectionHappened.Subscribe(async _ =>
            {
                await Reconnect();
            });

            _client.ReconnectionHappened.Subscribe(async _ =>
            {
                await Reconnect();
            });
            _client.ErrorReconnectTimeout = TimeSpan.FromSeconds(3);
            _client.ReconnectTimeout = TimeSpan.FromSeconds(3);
            _client.MessageReceived.
                Where(rm => !string.IsNullOrEmpty(rm.Text) && rm.Text.StartsWith('[')).
                Subscribe(OnMessageHandler);

            _initialized = true;
        }

        private async Task Reconnect()
        {
            try
            {
                if (!_client.IsRunning)
                {
                    OnDisconnected?.Invoke();
                }
                await _client.Start();
                await _client.SendInstant("[5, \"OnJsonApiEvent\"]");
                SendMessage();
                if (_client.IsRunning)
                {
                    OnConnected?.Invoke();
                }
            }
            catch (Exception e)
            {
            }

        }

        private void SendMessage()
        {
            if (_loopAlive)
            {
                return;
            }
            _loopAlive = true;
            Task.Run(async () =>
            {
                while (true)
                {
                    await _client.SendInstant("[5, \"OnJsonApiEvent\"]");
                    await Task.Delay(2000);
                }
            });
        }

        public async Task ConnectAsync()
        {
            try
            {
                if (_connected || !_initialized)
                {
                    return;
                }
                await _client.Start();
                await _client.SendInstant("[5, \"OnJsonApiEvent\"]");
                if (_client.IsRunning)
                {
                    OnConnected?.Invoke();
                }
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

        private void OnMessageHandler(ResponseMessage responseMessage)
        {
            var payload = JArray.Parse(responseMessage.Text);
            if (payload.Count != 3)
            {
                return;
            }
            /*
             [8,"OnJsonApiEvent",{"data":[],"eventType":"Update","uri":"/lol-ranked/v1/notifications"}]
             */
            ;
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

        public void Close()
        {
            _client?.Dispose();
        }
    }
}

using Prometheus.Core.Models;
using Prometheus.Services.Interfaces.Client;
using System;

namespace Prometheus.Services.Client
{
    public class LeagueClient(IClientService clientService) : ILeagueClient
    {
        private bool _connected;
        public bool Connected => _connected;

        public event Action OnConnected;

        public event Action OnDisconnected;

        public event Action<OnWebsocketEventArgs> OnWebsocketEvent;


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

                }

            }
            catch (Exception)
            {

                throw;
            }
        }

        internal class ClientParameter
        {
            public string Port { get; set; }

            public string Token { get; set; }
        }
    }
}

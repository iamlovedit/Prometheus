using Prometheus.Core.Models;
using System;
using System.Threading.Tasks;

namespace Prometheus.Services.Interfaces.Client
{
    public interface ILeagueClient
    {
        event Action OnConnected;

        event Action OnDisconnected;

        event Action<OnWebsocketEventArgs> OnWebsocketEvent;

        bool Connected { get; }

        string Port { get; protected set; }

        string Token { get; protected set; }

        int ProcessId { get; protected set; }

        void Subscribe(string uri, Action<OnWebsocketEventArgs> args);

        void Unsubscribe(string uri, Action<OnWebsocketEventArgs> action);

        Task<bool> StartAsync();
    }
}
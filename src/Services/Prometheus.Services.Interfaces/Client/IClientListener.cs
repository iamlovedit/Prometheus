using Prometheus.Core.Models;

namespace Prometheus.Services.Interfaces.Client
{
    public interface IClientListener
    {
        event Action OnConnected;

        event Action OnDisconnected;

        event Action<OnWebsocketEventArgs> OnWebsocketEvent;

        Task ConnectAsync();

        void Close();

        void Subscribe(string uri, Action<OnWebsocketEventArgs> args);

        void Unsubscribe(string uri, Action<OnWebsocketEventArgs> action);

        bool IsConnected { get; }

        void Initialize(string port, string token);
    }
}

using System;

namespace Prometheus.Services.Interfaces
{
    public interface IClientListener
    {
        event Action OnConnected;

        event Action OnDisconnected;

        event Action<OnWebsocketEventArgs> OnWebsocketEvent;

        void Connect(ushort port, string token);

        void Subscribe(string uri, Action<OnWebsocketEventArgs> args);

        void Unsubscribe(string uri, Action<OnWebsocketEventArgs> action);

        bool IsConnected { get; }
    }

    public class OnWebsocketEventArgs : EventArgs
    {
        // URI    
        public string Path { get; set; }

        // Update create delete     
        public string Type { get; set; }

        // data :D
        public dynamic Data { get; set; }
    }
}

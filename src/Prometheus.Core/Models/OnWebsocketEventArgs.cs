using System;

namespace Prometheus.Core.Models
{
    public class OnWebsocketEventArgs : EventArgs
    {
        public dynamic Data { get; set; }

        public string EventType { get; set; }

        public string Uri { get; set; }
    }
}

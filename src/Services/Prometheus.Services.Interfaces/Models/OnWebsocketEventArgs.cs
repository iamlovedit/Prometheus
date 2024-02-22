using Newtonsoft.Json;
using System;

namespace Prometheus.Services.Interfaces.Models
{
    public class OnWebsocketEventArgs : EventArgs
    {
        [JsonProperty("data")]
        public dynamic Data { get; set; }

        [JsonProperty("eventType")]
        public string EventType { get; set; }

        [JsonProperty("uri")]
        public string Uri { get; set; }
    }
}

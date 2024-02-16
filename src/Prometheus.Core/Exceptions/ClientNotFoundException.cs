using System;
using System.Runtime.Serialization;

namespace Prometheus.Core.Exceptions
{
    [Serializable]
    public class ClientNotFoundException : Exception
    {
        public ClientNotFoundException() { }
        public ClientNotFoundException(string message) : base(message) { }
        public ClientNotFoundException(string message, Exception inner) : base(message, inner) { }
        protected ClientNotFoundException(
          SerializationInfo info,
          StreamingContext context) : base(info, context) { }
    }
}

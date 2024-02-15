using Prometheus.Services.Interfaces;

namespace Prometheus.Services
{
    public class MessageService : IMessageService
    {
        public string GetMessage()
        {
            return "Hello from the Message Service";
        }
    }
}

using Prism.Events;

namespace Prometheus.Core.Events
{
    public class TitleChangeEvent : PubSubEvent<string>
    {

    }

    public class WindowClosingEvent : PubSubEvent
    {

    }

    public class ConnectLCUEvent : PubSubEvent
    {

    }

    public class DisConnectLCUEvent: PubSubEvent
    {

    }
}

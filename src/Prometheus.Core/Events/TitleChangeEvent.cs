using Prism.Events;

namespace Prometheus.Core.Events
{
    public class TitleChangeEvent : PubSubEvent<string>
    {

    }

    public class WindowClosingEvent : PubSubEvent
    {

    }

    public class ConnectLCUEvent : PubSubEvent<bool>
    {

    }

    public class LanguageSwitchedEvent : PubSubEvent
    {

    }
    public class MatchStartEvent : PubSubEvent<bool>
    {

    }
}

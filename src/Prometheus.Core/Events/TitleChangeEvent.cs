using Prism.Events;
using Prometheus.Core.Models;

namespace Prometheus.Core.Events
{
    public class TitleChangeEvent : PubSubEvent<string>
    {

    }

    public class WindowClosingEvent : PubSubEvent
    {

    }

    public class ApplicationExitRequestedEvent : PubSubEvent
    {
    }

    public class ShowMainWindowEvent : PubSubEvent
    {
    }

    public class LanguageSwitchedEvent : PubSubEvent
    {

    }
    public class MatchStartEvent : PubSubEvent<bool>
    {

    }

    public class SearchSummonerEvent : PubSubEvent<SummonerAccount>
    {

    }

    /// <summary>
    /// Requests navigation from a feature module without coupling it to the shell's
    /// module-loading and title-management implementation.
    /// </summary>
    public class NavigateMenuEvent : PubSubEvent<MenuName>
    {
    }
}

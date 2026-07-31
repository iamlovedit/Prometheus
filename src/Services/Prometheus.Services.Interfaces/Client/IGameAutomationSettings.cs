using System.ComponentModel;

namespace Prometheus.Services.Interfaces.Client
{
    public interface IGameAutomationSettings : INotifyPropertyChanged
    {
        bool AutoAcceptReadyCheck { get; set; }

        bool AutoReconnect { get; set; }

        bool AutoAccept { get; set; }

        bool IsAutoAcceptEnabled { get; set; }

        bool IsAutoReconnectEnabled { get; set; }

        bool AutoSwapAramBench { get; set; }

        IReadOnlyList<int> PreferredAramChampionIds { get; set; }

        bool LastPersistenceSucceeded { get; }

        event EventHandler Changed;
    }
}

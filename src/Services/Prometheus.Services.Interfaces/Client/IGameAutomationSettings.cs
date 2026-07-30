using System;
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

        event EventHandler Changed;
    }
}

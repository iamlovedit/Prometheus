using System.ComponentModel;

namespace Prometheus.Services.Interfaces.Client
{
    public interface ILcuCompanionSettings : INotifyPropertyChanged
    {
        bool IsEnabled { get; set; }

        bool AutoShowMatchOnGameStart { get; set; }

        bool LastPersistenceSucceeded { get; }
    }
}

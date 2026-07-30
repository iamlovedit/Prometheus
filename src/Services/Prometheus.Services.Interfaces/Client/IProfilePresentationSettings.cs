using Prometheus.Core.Models;

namespace Prometheus.Services.Interfaces.Client
{
    public interface IProfilePresentationSettings
    {
        string OnlineStatus { get; }

        string StatusMessage { get; }

        QueueType? QueueType { get; }

        Tier? Tier { get; }

        Division? Division { get; }

        void SaveOnlineStatus(string onlineStatus);

        void SaveStatusMessage(string statusMessage);

        void SaveTier(QueueType queueType, Tier tier, Division division);
    }
}

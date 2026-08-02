namespace Prometheus.Core.Models
{
    public enum MatchmadeLobbyCreationStatus
    {
        Created,
        ClientUnavailable,
        QueueUnavailable,
        OperationInProgress,
        LobbyNotConfirmed
    }

    public sealed class MatchmadeLobbyCreationResult
    {
        public MatchmadeLobbyCreationStatus Status { get; init; }

        public int QueueId { get; init; }

        public LobbySnapshot Lobby { get; init; }

        public bool Succeeded => Status == MatchmadeLobbyCreationStatus.Created;
    }
}

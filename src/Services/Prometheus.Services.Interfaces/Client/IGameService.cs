using Prometheus.Core.Models;

namespace Prometheus.Services.Interfaces.Client
{
    public interface IGameService
    {
        Task CreateRunePage(object body);

        Task<string> GetCurrentRunePage();

        Task<string> GetAllRunePages();

        Task DeleteRunePage(long id);

        Task AcceptMatchAsync();

        Task<string> GetGameSessionAsync();

        Task PickChampionAsync(int actionId, int championId);

        Task BanChampionAsync(int actionId, int championId);

        Task<MatchDetail> GetMatchDetailAsync(long gameId);

        Task<string> GetCurrentGameInfoAsync();

        Task<string> GetCurrentChampionInfoAsync();

        Task<byte[]> GetResourceByUrl(string url);

        Task<string> GetItems();

        Task<string> GetProfileIcons();

        Task<string> GetSpells();

        Task<string> GetRuneItemsFromOnlineAsync(int championId);

        Task<string> GetPickableChampionsAsync();

        Task<string> GetChampionRankAsync(string lane, int tier, int time);

        Task<string> SetSkinAsync(object body);

        Task<string> SetIconAsync(object body);

        Task<string> GetChampionSkinById(int id);

        Task CreatePracticeLobbyAsync(string name, string password);

        Task<string> SetChatTierAsync(QueueType queueType, Tier tier, Division division);

        Task SetOnlineStatusAsync(string chatStatus);

        Task SetStatusAsync(string status);

        Task ReconnectGameAsync();

        Task<string> GetAcceptStatusAsync();

        Task<string> GetMapSideAsync();

        Task<GameflowSessionSnapshot> GetGameflowSessionSnapshotAsync(
            CancellationToken cancellationToken = default);

        Task<string> GetGameflowPhaseAsync(CancellationToken cancellationToken = default);

        Task<LobbySnapshot> GetLobbySnapshotAsync(CancellationToken cancellationToken = default);

        Task<MatchmakingSnapshot> GetMatchmakingSnapshotAsync(CancellationToken cancellationToken = default);

        Task<ReadyCheckSnapshot> GetReadyCheckSnapshotAsync(CancellationToken cancellationToken = default);

        Task<ChampionSelectSnapshot> GetChampionSelectSnapshotAsync(
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Requests an ARAM bench swap through
        /// lol-champ-select/v1/session/bench/swap/{championId}. When LCU is not
        /// initialized the underlying HTTP service completes without sending a request.
        /// </summary>
        Task SwapAramBenchChampionAsync(
            int championId,
            CancellationToken cancellationToken = default);

        Task<PostGameSnapshot> GetPostGameSnapshotAsync(CancellationToken cancellationToken = default);

        Task AcceptMatchAsync(CancellationToken cancellationToken);

        Task ReconnectGameAsync(CancellationToken cancellationToken);
    }
}

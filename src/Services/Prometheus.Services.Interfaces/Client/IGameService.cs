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

        /// <summary>
        /// Returns champion ids currently accepted by the active pick action. The current
        /// <c>pickable-champion-ids</c> endpoint is used with the legacy endpoint as a
        /// compatibility fallback. Returns an empty list when LCU is unavailable.
        /// </summary>
        Task<IReadOnlyList<int>> GetPickableChampionIdsAsync(
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Returns champion ids currently accepted by the active ban action. The current
        /// <c>bannable-champion-ids</c> endpoint is used with the legacy endpoint as a
        /// compatibility fallback. Returns an empty list when LCU is unavailable.
        /// </summary>
        Task<IReadOnlyList<int>> GetBannableChampionIdsAsync(
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Updates the local player's active champion-select action and explicitly completes
        /// it through <c>lol-champ-select/v1/session/actions/{id}/complete</c>.
        /// </summary>
        Task CompleteChampionSelectActionAsync(
            ChampionSelectActionSnapshot action,
            int championId,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Gets a match from <c>lol-match-history/v1/games/{gameId}</c> and resolves
        /// its display mode from LCU queue metadata when available.
        /// Returns <see langword="null"/> when LCU is unavailable and supports cancellation.
        /// </summary>
        Task<MatchDetail> GetMatchDetailAsync(long gameId,
            CancellationToken cancellationToken = default);

        Task<string> GetCurrentGameInfoAsync();

        Task<string> GetCurrentChampionInfoAsync();

        Task<byte[]> GetResourceByUrl(string url);

        Task<string> GetItems();

        Task<string> GetProfileIcons();

        Task<string> GetSpells();

        Task<string> GetRuneItemsFromOnlineAsync(int championId);

        /// <summary>
        /// Gets rune recommendations for a champion from public QQ/WeGame data.
        /// Summoner's Rift prefers the assigned lane while ARAM uses its dedicated
        /// recommendation set. Returns <see langword="null"/> when LCU HTTP is not
        /// initialized, no current recommendation can be parsed, or the request fails.
        /// </summary>
        Task<RuneRecommendationSet> GetRuneRecommendationsAsync(
            int championId,
            string assignedPosition,
            bool isAram,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Creates or updates the Prometheus-managed rune page identified by a name
        /// ending in <c>[Prometheus]</c> and confirms through
        /// <c>lol-perks/v1/currentpage</c> that it became the active page. Other player
        /// rune pages are never modified. Supports cancellation.
        /// </summary>
        Task<RunePageApplyResult> ApplyRuneRecommendationAsync(
            string managedPageName,
            RuneRecommendationOption recommendation,
            CancellationToken cancellationToken = default);

        Task<string> GetPickableChampionsAsync();

        Task<string> GetChampionRankAsync(string lane, int tier, int time);

        Task<string> SetSkinAsync(object body);

        Task<string> SetIconAsync(object body);

        Task<string> GetChampionSkinById(int id);

        Task CreatePracticeLobbyAsync(string name, string password);

        /// <summary>
        /// Creates a matchmade lobby, or changes an existing lobby queue,
        /// through <c>lol-lobby/v2/lobby</c> after confirming that the requested
        /// LCU queue is available. Returns a status result when LCU is unavailable,
        /// the queue is disabled, or the resulting lobby cannot be confirmed.
        /// Supports cancellation.
        /// </summary>
        Task<MatchmadeLobbyCreationResult> CreateMatchmadeLobbyAsync(
            int queueId,
            CancellationToken cancellationToken = default);

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

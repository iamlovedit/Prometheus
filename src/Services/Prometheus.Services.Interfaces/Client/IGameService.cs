using Prometheus.Core.Models;
using System.Threading.Tasks;

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
    }
}

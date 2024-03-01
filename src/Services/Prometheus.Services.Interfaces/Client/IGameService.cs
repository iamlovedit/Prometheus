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

        Task<string> GetSummonerSuperChampionDataAsync(long summonerId);

        Task<string> GetCurrentGameInfoAsync();

        Task<string> GetCurrentChampionInfoAsync();

        Task<byte[]> GetResourceByUrl(string url);

        Task<string> GetItems();

        Task<string> GetProfileIcons();

        Task<string> GetSpells();

        Task<string> GetMatchRecordsPage(string puuid, int pageStart, int pageEnd);

        Task<string> GetRuneItemsFromOnlineAsync(int championId);

        Task<string> GetPickableChampionsAsync();

        Task<string> GetChampionRankAsync(string lane, int tier, int time);

        Task<string> SetSkinAsync(object body);

        Task<string> SetIconAsync(object body);

        Task<string> GetChampionSkinById(int id);

        Task CreatePracticeLobby(string name, string password);
    }
}

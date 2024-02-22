using System.Threading.Tasks;

namespace Prometheus.Services.Interfaces.Client
{
    public interface IGameService
    {
        Task CreateRunePage(object body);

        Task<string> GetCurrentRunePage();

        Task<string> GetAllRunePages();

        Task DeleteRunePage(long id);

        Task SetAutoAcceptAsync();

        Task<string> GetGameSessionAsync();

        Task SetAutoLockChampionAsync(int actionId, int championId, string type);

        Task<string> GetMatchDetailAsync(long gameId);

        Task<string> GetSummonerSuperChampionDataAsync(long summonerId);

        Task<string> GetCurrentGameInfoAsync();

        Task<string> GetCurrentChampionInfoAsync();

        Task<byte[]> GetResourceByUrl(string url);

        Task<string> GetItems();

        Task<string> GetProfileIcons();

        Task<string> GetSpells();

        Task<string> GetMatchRecordsPage(int pageStart = 0, int pageEnd = 20, string id = null);

        Task<string> GetRuneItemsFromOnlineAsync(int championId);

        Task<string> GetPickableChampionsAsync();

        Task<string> GetChampRankAsync(string lane, int tier, int time);

        Task<string> SetSkinAsync(object body);

        Task<string> SetIconAsync(object body);

        Task<string> GetChampionSkinById(int id);
    }
}

using Prometheus.Services.Interfaces;
using Prometheus.Services.Interfaces.Client;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace Prometheus.Services.Client
{
    public class ClientService : IClientService
    {
        [DllImport("shlwapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool PathCanonicalize(StringBuilder dst, string src);

        private readonly string _client = "riotclient/{0}";
        private readonly IHttpService _httpService;
        public ClientService(IHttpService httpService)
        {
            _httpService = httpService;
        }

        public async Task<string> GetInstallLocation()
        {
            var path = await _httpService.GetAsync("data-store/v1/install-dir");
            return ConvertPath(path);
        }

        public async Task QuitClientAsync()
        {
            await _httpService.PostAsync(string.Format(_client, "unload"), null);
        }

        private static string ConvertPath(string path)
        {
            var pathParts = path.Split(':');
            return path.Replace(pathParts[0], pathParts[0].ToUpper()).Replace(@"\\", @"\");
        }

        public async Task<string> GetQueuesAsync()
        {
            return await _httpService.GetAsync("lol-game-queues/v1/queues");
        }

        public async Task SetForgeground()
        {
            await _httpService.PostAsync(string.Format(_client, "ux-show"), null);
        }

        public async Task FlashClient()
        {
            await _httpService.PostAsync(string.Format(_client, "ux-flash"), null);
        }

        public async Task MinimizeClient()
        {
            await _httpService.PostAsync(string.Format(_client, "ux-minimize"), null);
        }
    }
}

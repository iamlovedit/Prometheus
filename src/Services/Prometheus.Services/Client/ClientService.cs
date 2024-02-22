using Prometheus.Services.Interfaces;
using Prometheus.Services.Interfaces.Client;
using System.Threading.Tasks;

namespace Prometheus.Services.Client
{
    public class ClientService : IClientService
    {
        private readonly string _processUrl = "process-control/v1/process/";

        private readonly IHttpService _httpService;
        public ClientService(IHttpService httpService)
        {
            _httpService = httpService;
        }

        public async Task<string> GetInstallLocation()
        {
            return await _httpService.GetAsync("data-store/v1/install-dir");
        }

        public async Task QuitClientAsync()
        {
            await _httpService.PostAsync($"{_processUrl}quit", null);
        }
    }
}

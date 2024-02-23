using Prism.Regions;
using Prometheus.Core.Mvvm;
using Prometheus.Services.Interfaces.Client;

namespace Prometheus.Modules.Inventory.ViewModels
{
    public class InventoryViewModel : RegionViewModelBase
    {
        private readonly IClientService _clientService;

        public InventoryViewModel(IRegionManager regionManager, IClientService clientService) : base(regionManager)
        {
            _clientService = clientService;
            Initialize();
        }

        private async void Initialize()
        {
            var items = await _clientService.GetItemsAsync();
            var skins = await _clientService.GetSkinsAsync();
            var queues = await _clientService.GetQueuesAsync();
            var champions = await _clientService.GetChampionSummarysAsync();
            var runes = await _clientService.GetPerksAsync();
        }
    }
}

using Prism.Regions;
using Prometheus.Core.Mvvm;
using Prometheus.Services.Interfaces.Client;
namespace Prometheus.Modules.Inventory.ViewModels
{
    public class InventoryViewModel : RegionViewModelBase
    {
        private readonly IGameResourceManager _gameResourceManager;

        public InventoryViewModel(IRegionManager regionManager, IGameResourceManager gameResourceManager) : base(regionManager)
        {
            _gameResourceManager = gameResourceManager;
            Initialize();
        }

        private async void Initialize()
        {
            var items = await _gameResourceManager.GetItemsAsync();
            var skins = await _gameResourceManager.GetSkinsAsync();
            var champions = await _gameResourceManager.GetChampionSummarysAsync();
            var runes = await _gameResourceManager.GetPerksAsync();
        }
    }
}

using Prism.Regions;
using Prometheus.Core.Mvvm;
using Prometheus.Services.Interfaces.Client;
using System.Threading.Tasks;
namespace Prometheus.Modules.Inventory.ViewModels
{
    public class InventoryViewModel : RegionViewModelBase
    {
        private readonly IGameResourceManager _gameResourceManager;

        public InventoryViewModel(IRegionManager regionManager, IGameResourceManager gameResourceManager) : base(regionManager)
        {
            _gameResourceManager = gameResourceManager;
            
        }

        private async Task Initialize()
        {
     
        }
    }
}

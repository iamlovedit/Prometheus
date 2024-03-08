using Prism.Ioc;
using Prism.Regions;
using Prometheus.Core.Models;
using Prometheus.Core.Mvvm;
using Prometheus.Modules.Match.Views;

namespace Prometheus.Modules.Match
{
    public class MatchModule : ModuleBase
    {
        public MatchModule(IRegionManager regionManager) : base(regionManager)
        {
        }

        public override void OnInitialized(IContainerProvider containerProvider)
        {

        }

        public override void RegisterTypes(IContainerRegistry containerRegistry)
        {
            containerRegistry.RegisterForNavigation<MatchView>(MenuName.Match.ToString());
        }
    }
}
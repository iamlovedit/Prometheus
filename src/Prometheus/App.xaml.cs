using Prism.Ioc;
using Prism.Modularity;
using Prometheus.Modules.Home;
using Prometheus.Modules.Inventory;
using Prometheus.Modules.Match;
using Prometheus.Modules.Search;
using Prometheus.Modules.Setting;
using Prometheus.Modules.Summoner;
using Prometheus.Modules.Utility;
using Prometheus.Views;
using System.Windows;

namespace Prometheus
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App
    {
        protected override Window CreateShell()
        {
            return Container.Resolve<MainWindow>();
        }

        protected override void RegisterTypes(IContainerRegistry containerRegistry)
        {

        }

        protected override void ConfigureModuleCatalog(IModuleCatalog moduleCatalog)
        {
            moduleCatalog.AddModule<SummonerModule>();
            moduleCatalog.AddModule<MatchModule>();
            moduleCatalog.AddModule<HomeModule>();
            moduleCatalog.AddModule<SettingModule>();
            moduleCatalog.AddModule<InventoryModule>();
            moduleCatalog.AddModule<SearchModule>();
            moduleCatalog.AddModule<UtilityModule>();
        }
    }
}

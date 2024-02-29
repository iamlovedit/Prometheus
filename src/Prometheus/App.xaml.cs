using Prism.Ioc;
using Prism.Modularity;
using Prometheus.Core;
using Prometheus.Modules.Home;
using Prometheus.Modules.Inventory;
using Prometheus.Modules.Match;
using Prometheus.Modules.Search;
using Prometheus.Modules.Setting;
using Prometheus.Modules.Summoner;
using Prometheus.Modules.Utility;
using Prometheus.Services;
using Prometheus.Services.Client;
using Prometheus.Services.Interfaces;
using Prometheus.Services.Interfaces.Client;
using Prometheus.Shared.Views;
using Prometheus.Views;
using Serilog;
using System.IO;
using System.Reflection;
using System.Windows;

namespace Prometheus
{
    public partial class App
    {
        public App()
        {
            Log.Logger = new LoggerConfiguration().WriteTo.File("log-.txt",
                rollingInterval: RollingInterval.Day,
                outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {Message:lj}{NewLine}{Exception}").CreateLogger();
        }
        protected override Window CreateShell()
        {
            return Container.Resolve<MainWindow>();
        }

        protected override void RegisterTypes(IContainerRegistry containerRegistry)
        {
            containerRegistry.RegisterSingleton<IHttpService, HttpService>();
            containerRegistry.RegisterSingleton<IClientListener, ClientListener>();
            containerRegistry.RegisterSingleton<IResourceService, ResourceService>();
            containerRegistry.RegisterSingleton<IClientService, ClientService>();
            containerRegistry.RegisterSingleton<IGameService, GameService>();
            containerRegistry.RegisterSingleton<IGameResourceManager, GameResourceManager>();
            containerRegistry.RegisterSingleton<ISummonerService, SummonerService>();
            containerRegistry.RegisterSingleton<IMatchService, MatchService>();
            containerRegistry.RegisterForNavigation<MatchHistoryView>(RegionNames.MatchHistoryView);
            containerRegistry.RegisterForNavigation<SummonerDetailView>(RegionNames.SummonerDetailView);
            containerRegistry.RegisterDialogWindow<DialogWindow>();

            var directory = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            directory = Path.Combine(directory, "Resource");
            if (!Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }
            var subDirectories = new string[6]
            { ParameterNames.Equipments, ParameterNames.Perks, ParameterNames.Skins, ParameterNames.Spells, ParameterNames.ChampoinIcon, ParameterNames.ProfileIcon };
            foreach (var dirName in subDirectories)
            {
                var subDir = Path.Combine(directory, dirName);
                if (!Directory.Exists(subDir))
                {
                    Directory.CreateDirectory(subDir);
                }
                containerRegistry.RegisterInstance(subDir, dirName);
            }
            containerRegistry.RegisterInstance(directory, ParameterNames.LocalResourceDirectory);
        }

        protected override void ConfigureModuleCatalog(IModuleCatalog moduleCatalog)
        {
            moduleCatalog.AddModule<HomeModule>();
            moduleCatalog.AddModule<SettingModule>();
            moduleCatalog.AddModule<SummonerModule>(InitializationMode.OnDemand);
            moduleCatalog.AddModule<MatchModule>(InitializationMode.OnDemand);
            moduleCatalog.AddModule<InventoryModule>(InitializationMode.OnDemand);
            moduleCatalog.AddModule<SearchModule>(InitializationMode.OnDemand);
            moduleCatalog.AddModule<UtilityModule>(InitializationMode.OnDemand);
        }
    }
}

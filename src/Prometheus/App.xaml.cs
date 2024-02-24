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
using Prometheus.Views;
using Serilog;
using System;
using System.IO;
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
            containerRegistry.RegisterSingleton<IProcessService, ProcessService>();
            containerRegistry.RegisterSingleton<IHttpService, HttpService>();
            containerRegistry.RegisterSingleton<IClientListener, ClientListener>();
            containerRegistry.RegisterSingleton<IResourceService, ResourceService>();
            containerRegistry.RegisterSingleton<IClientService, ClientService>();
            containerRegistry.RegisterSingleton<IGameService, GameService>();
            containerRegistry.RegisterSingleton<IGameResourceManager, GameResourceManager>();

            var directory = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            directory = Path.Combine(directory, "Prometheus", "Resource");
            if (!Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }
            containerRegistry.RegisterInstance(directory, ParameterNames.LocalResourceDirectory);
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

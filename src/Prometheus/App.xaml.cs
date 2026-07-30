using Prism.DryIoc;
using Prism.Ioc;
using Prism.Modularity;
using Prometheus.Core;
using Prometheus.Core.Models;
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
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;

namespace Prometheus
{
    public partial class App : PrismApplication
    {
        [DllImport("user32.dll")]
        public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        [DllImport("user32.dll")]
        public static extern bool SetForegroundWindow(IntPtr hWnd);

        private LogHistoryService _logHistory;

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
            containerRegistry.RegisterSingleton<IGameAutomationSettings, GameAutomationSettings>();
            containerRegistry.RegisterSingleton<IMatchService, MatchService>();
            containerRegistry.RegisterSingleton<ILeagueClient, LeagueClient>();
            containerRegistry.RegisterInstance<ILogHistoryService>(_logHistory);
            containerRegistry.RegisterForNavigation<MatchHistoryView>(RegionNames.MatchHistoryView);
            containerRegistry.RegisterForNavigation<SummonerDetailView>(RegionNames.SummonerDetailView);
            containerRegistry.RegisterDialogWindow<DialogWindow>();
            containerRegistry.RegisterInstance<Dictionary<int, List<SkinBasic>>>([], ParameterNames.SkinsCache);


            var directory = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            directory = Path.Combine(directory, "Resource");
            if (!Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var subDirectories = new string[6]
            {
                ParameterNames.Equipments, ParameterNames.Perks, ParameterNames.Skins, ParameterNames.Spells,
                ParameterNames.ChampoinIcon, ParameterNames.ProfileIcon
            };
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

        protected override void OnStartup(StartupEventArgs e)
        {
            var currentProcessName = Process.GetCurrentProcess().ProcessName;
            var existingProcess = Process.GetProcessesByName(currentProcessName)
                .FirstOrDefault(p => p.Id != System.Environment.ProcessId);
            if (existingProcess != null)
            {
                var mainWindowHandle = existingProcess.MainWindowHandle;
                ShowWindow(mainWindowHandle, 9);
                SetForegroundWindow(mainWindowHandle);
                Environment.Exit(0);
            }
            else
            {
                _logHistory = new LogHistoryService(1000);
                Log.Logger = new LoggerConfiguration()
                    .WriteTo.File("log-.txt",
                        rollingInterval: RollingInterval.Day,
                        outputTemplate:
                        "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {Message:lj}{NewLine}{Exception}")
                    .WriteTo.Sink(_logHistory.Sink)
                    .CreateLogger();
                RegisterExceptionHandlers();
                base.OnStartup(e);
            }
        }

        private void RegisterExceptionHandlers()
        {
            DispatcherUnhandledException += HandleDispatcherUnhandledException;
            TaskScheduler.UnobservedTaskException += HandleUnobservedTaskException;
            AppDomain.CurrentDomain.UnhandledException += HandleUnhandledException;
        }

        private static void HandleDispatcherUnhandledException(object sender,
            DispatcherUnhandledExceptionEventArgs args)
        {
            if (IsRecoverableClientFailure(args.Exception))
            {
                Log.Warning(args.Exception,
                    "Recovered from an unhandled League client transport failure on the UI thread");
                args.Handled = true;
                return;
            }

            Log.Fatal(args.Exception, "Unhandled UI thread exception");
        }

        private static void HandleUnobservedTaskException(object sender,
            UnobservedTaskExceptionEventArgs args)
        {
            Log.Error(args.Exception, "Unobserved background task exception");
            args.SetObserved();
        }

        private static void HandleUnhandledException(object sender, UnhandledExceptionEventArgs args)
        {
            if (args.ExceptionObject is Exception exception)
            {
                Log.Fatal(exception, "Unhandled application exception");
            }
        }

        private static bool IsRecoverableClientFailure(Exception exception)
        {
            if (exception is AggregateException aggregateException)
            {
                var innerExceptions = aggregateException.Flatten().InnerExceptions;
                return innerExceptions.Count > 0 && innerExceptions.All(IsRecoverableClientFailure);
            }

            for (var current = exception; current is not null; current = current.InnerException)
            {
                if (current is HttpRequestException or OperationCanceledException or ObjectDisposedException)
                {
                    return true;
                }
            }

            return false;
        }
    }
}

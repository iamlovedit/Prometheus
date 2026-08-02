using Prism.DryIoc;
using Prism.Ioc;
using Prism.Modularity;
using Prometheus.Core;
using Prometheus.Core.Logging;
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
using Prometheus.Services.Interfaces.Updates;
using Prometheus.Services.Updates;
using Prometheus.Shared.Views;
using Prometheus.Update;
using Prometheus.ViewModels;
using Prometheus.Views;
using Prometheus.Properties;
using Serilog;
using Serilog.Events;
using Serilog.Formatting.Json;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Threading;

namespace Prometheus
{
    public partial class App : PrismApplication
    {
        private const int RetainedLogFileCount = 7;
        private const string LogFileSearchPattern = "prometheus-*.jsonl";
        private static readonly TimeSpan LogFileRetentionPeriod = TimeSpan.FromDays(7);

        [DllImport("user32.dll")]
        public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        [DllImport("user32.dll")]
        public static extern bool SetForegroundWindow(IntPtr hWnd);

        private LogHistoryService _logHistory;
        private LoggingControlService _loggingControl;

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
            containerRegistry.RegisterSingleton<IProfilePresentationSettings, ProfilePresentationSettings>();
            containerRegistry.RegisterSingleton<IQuickMatchSettings, QuickMatchSettings>();
            containerRegistry.RegisterSingleton<IGameResourceManager, GameResourceManager>();
            containerRegistry.RegisterSingleton<ISummonerService, SummonerService>();
            containerRegistry.RegisterSingleton<IGameAutomationSettings, GameAutomationSettings>();
            containerRegistry.RegisterSingleton<IMatchService, MatchService>();
            containerRegistry.RegisterSingleton<IProfilePresentationStartupService, ProfilePresentationStartupService>();
            containerRegistry.RegisterSingleton<ILeagueClient, LeagueClient>();
            containerRegistry.RegisterInstance(UpdateRuntime.CreateOptions());
            containerRegistry.RegisterSingleton<IUpdateService, UpdateService>();
            containerRegistry.RegisterInstance<ILogHistoryService>(_logHistory);
            containerRegistry.RegisterInstance<ILoggingControlService>(_loggingControl);
            containerRegistry.RegisterForNavigation<MatchHistoryView>(RegionNames.MatchHistoryView);
            containerRegistry.RegisterForNavigation<SummonerDetailView>(RegionNames.SummonerDetailView);
            containerRegistry.RegisterDialogWindow<DialogWindow>();
            containerRegistry.RegisterDialog<UpdateDialog, UpdateDialogViewModel>(RegionNames.UpdateDialog);
            containerRegistry.RegisterInstance<Dictionary<int, List<SkinBasic>>>([], ParameterNames.SkinsCache);


            var legacyDirectory = Path.Combine(AppContext.BaseDirectory, "Resource");
            var directory = Path.Combine(UpdatePaths.GetLocalDataRoot(), "Resource");
            MigrateLegacyResources(legacyDirectory, directory);
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
                var logDirectory = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "Prometheus",
                    "Logs");
                var cleanupResult = LogFileRetentionCleaner.DeleteExpiredFiles(
                    logDirectory,
                    LogFileSearchPattern,
                    LogFileRetentionPeriod,
                    DateTimeOffset.UtcNow);

                _logHistory = new LogHistoryService(5000);
                _loggingControl = new LoggingControlService(
                    Settings.Default.EnableLogging,
                    _logHistory,
                    PersistLoggingSetting);
#if DEBUG
                var minimumLogLevel = LogEventLevel.Debug;
#else
                var minimumLogLevel = LogEventLevel.Information;
#endif
                Log.Logger = new LoggerConfiguration()
                    .MinimumLevel.Is(minimumLogLevel)
                    .Filter.With(_loggingControl)
                    .WriteTo.Sink(new LoggingControlledSink(
                        _loggingControl,
                        new DeferredFileLogSink(
                            Path.Combine(
                                logDirectory,
                                "prometheus-.jsonl"),
                            new JsonFormatter(renderMessage: true),
                            RollingInterval.Day,
                            RetainedLogFileCount,
                            LogFileRetentionPeriod)))
                    .WriteTo.Sink(new LoggingControlledSink(
                        _loggingControl,
                        _logHistory.Sink))
                    .CreateLogger();
                ReportLogCleanupResult(cleanupResult);
                RegisterExceptionHandlers();
                TextInputContextMenu.Register();
                base.OnStartup(e);
            }
        }

        protected override void OnExit(ExitEventArgs e)
        {
            try
            {
                base.OnExit(e);
            }
            finally
            {
                Log.CloseAndFlush();
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
                GlobalExceptionLog.Write(
                    Log.Logger,
                    LogEventLevel.Warning,
                    "application.exception.ui.recovered",
                    "Dispatcher",
                    args.Exception,
                    isTerminating: false,
                    "Recovered from an unhandled League client transport failure on the UI thread");
                args.Handled = true;
                return;
            }

            GlobalExceptionLog.Write(
                Log.Logger,
                LogEventLevel.Fatal,
                "application.exception.ui.unhandled",
                "Dispatcher",
                args.Exception,
                isTerminating: true,
                "Unhandled UI thread exception");
            Log.CloseAndFlush();
        }

        private static void HandleUnobservedTaskException(object sender,
            UnobservedTaskExceptionEventArgs args)
        {
            GlobalExceptionLog.Write(
                Log.Logger,
                LogEventLevel.Error,
                "application.exception.task.unobserved",
                "TaskScheduler",
                args.Exception,
                isTerminating: false,
                "Unobserved background task exception");
            args.SetObserved();
        }

        private static void HandleUnhandledException(object sender, UnhandledExceptionEventArgs args)
        {
            GlobalExceptionLog.Write(
                Log.Logger,
                LogEventLevel.Fatal,
                "application.exception.domain.unhandled",
                "AppDomain",
                args.ExceptionObject,
                args.IsTerminating,
                "Unhandled application exception");

            if (args.IsTerminating)
            {
                Log.CloseAndFlush();
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

        private static void MigrateLegacyResources(string legacyDirectory, string targetDirectory)
        {
            Directory.CreateDirectory(targetDirectory);
            if (!Directory.Exists(legacyDirectory)
                || string.Equals(Path.GetFullPath(legacyDirectory), Path.GetFullPath(targetDirectory),
                    StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            foreach (var sourcePath in Directory.EnumerateFiles(legacyDirectory, "*",
                         SearchOption.AllDirectories))
            {
                var relativePath = Path.GetRelativePath(legacyDirectory, sourcePath);
                var destinationPath = Path.Combine(targetDirectory, relativePath);
                if (File.Exists(destinationPath))
                {
                    continue;
                }

                Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
                File.Copy(sourcePath, destinationPath);
            }
        }

        private static void PersistLoggingSetting(bool enabled)
        {
            Settings.Default.EnableLogging = enabled;
            Settings.Default.Save();
        }

        private static void ReportLogCleanupResult(LogFileCleanupResult result)
        {
            if (result.DeletedCount > 0)
            {
                Log.Information(
                    "Deleted {DeletedCount} application log files older than seven days",
                    result.DeletedCount);
            }

            if (result.FailureCount > 0)
            {
                Log.Warning(
                    "Failed to delete {FailureCount} expired application log files",
                    result.FailureCount);
            }
        }
    }
}

using Prism.Ioc;
using Prism.Modularity;
using Prometheus.Modules.ModuleName;
using Prometheus.Services;
using Prometheus.Services.Interfaces;
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
            containerRegistry.RegisterSingleton<IMessageService, MessageService>();
        }

        protected override void ConfigureModuleCatalog(IModuleCatalog moduleCatalog)
        {
            moduleCatalog.AddModule<ModuleNameModule>();
        }
    }
}

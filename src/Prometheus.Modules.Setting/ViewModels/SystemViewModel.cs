using Prism.Events;
using Prometheus.Services.Interfaces.Client;

namespace Prometheus.Modules.Setting.ViewModels
{
    public class SystemViewModel : TabItemViewModelBase
    {
        public SystemViewModel(IEventAggregator eventAggregator, IResourceService resourceService) : base(eventAggregator, resourceService)
        {
        }

        protected override string TitleResourceKey { get; set; } = "Setting.System";
    }
}

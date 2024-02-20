using Prism.Events;
using Prism.Mvvm;
using Prometheus.Core.Events;
using Prometheus.Services.Interfaces;
using System.Windows;

namespace Prometheus.Modules.Setting.ViewModels
{
    public class SystemViewModel : BindableBase
    {
        private string _title = Application.Current.FindResource("Setting.System")?.ToString();
        public string Title
        {
            get { return _title; }
            set { SetProperty(ref _title, value); }
        }
        private readonly IEventAggregator _eventAggregator;
        private readonly IResourceService _resourceService;
        public SystemViewModel(IEventAggregator eventAggregator, IResourceService resourceService)
        {
            _eventAggregator = eventAggregator;
            _resourceService = resourceService;
            _eventAggregator.GetEvent<LanguageSwitchedEvent>().Subscribe(() =>
            {
                Title = _resourceService.FindResource<string>("Setting.System");
            });
            _title = _resourceService.FindResource<string>("Setting.System");
        }
    }
}

using Prism.Events;
using Prism.Mvvm;
using Prometheus.Core.Events;
using Prometheus.Services.Interfaces.Client;

namespace Prometheus.Modules.Setting.ViewModels
{
    public abstract class TabItemViewModelBase : BindableBase
    {
        protected abstract string TitleResourceKey { get; set; }

        protected virtual IEventAggregator EventAggregator { get; }

        protected virtual IResourceService ResourceService { get; }
        public TabItemViewModelBase(IEventAggregator eventAggregator, IResourceService resourceService)
        {
            EventAggregator = eventAggregator;

            ResourceService = resourceService;
            Initialize();
        }

        private string _title;
        public string Title
        {
            get { return _title; }
            set { SetProperty(ref _title, value); }
        }

        protected virtual void Initialize()
        {
            EventAggregator.GetEvent<LanguageSwitchedEvent>().Subscribe(() =>
            {
                Title = ResourceService.FindResource<string>(TitleResourceKey);
            });

            _title = ResourceService.FindResource<string>(TitleResourceKey);
        }
    }
}

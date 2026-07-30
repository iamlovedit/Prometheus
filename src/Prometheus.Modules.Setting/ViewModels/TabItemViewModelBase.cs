using Prism.Events;
using Prometheus.Core.Events;
using Prometheus.Core.Mvvm;
using Prometheus.Services.Interfaces.Client;

namespace Prometheus.Modules.Setting.ViewModels
{
    public abstract class TabItemViewModelBase : ViewModelBase
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
            get => _title;
            set => SetProperty(ref _title, value);
        }

        protected virtual void Initialize()
        {
            EventAggregator.GetEvent<LanguageSwitchedEvent>().Subscribe(RefreshTitle);
            _title = ResourceService.FindResource<string>(TitleResourceKey);
        }

        public override void Destroy()
        {
            EventAggregator.GetEvent<LanguageSwitchedEvent>().Unsubscribe(RefreshTitle);
            base.Destroy();
        }

        private void RefreshTitle()
        {
            Title = ResourceService.FindResource<string>(TitleResourceKey);
        }
    }
}

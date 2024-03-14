using Prism.Commands;
using Prism.Events;
using Prism.Regions;
using Prometheus.Core;
using Prometheus.Core.Events;
using Prometheus.Core.Mvvm;
using Prometheus.Services.Interfaces.Client;
using Serilog;
using System;
using System.Net.Http;

namespace Prometheus.Modules.Search.ViewModels
{
    public class SearchViewModel : RegionViewModelBase
    {
        private readonly ISummonerService _summonerService;
        private readonly IEventAggregator _eventAggregtor;
        public SearchViewModel(IRegionManager regionManager, IEventAggregator eventAggregator, ISummonerService summonerService) : base(regionManager)
        {
            _summonerService = summonerService;
            _eventAggregtor = eventAggregator;

        }

        private bool _hasSummoner = true;
        public bool HasSummoner
        {
            get { return _hasSummoner; }
            set { SetProperty(ref _hasSummoner, value); }
        }


        private DelegateCommand<string> _searchCommand;
        public DelegateCommand<string> SearchCommand =>
            _searchCommand ?? (_searchCommand = new DelegateCommand<string>(ExecuteSearchCommand));
        async void ExecuteSearchCommand(string name)
        {
            try
            {
                if (string.IsNullOrEmpty(name))
                {
                    return;
                }
                var summoner = await _summonerService.SearchSummonerByName(name);
                _eventAggregtor.GetEvent<SearchSummonerEvent>().Publish(summoner);
            }
            catch (HttpRequestException)
            {
                HasSummoner = false;
            }
            catch (TimeoutException e)
            {
                Log.Error(e.Message);
            }
            catch (Exception e)
            {
                Log.Error(e.Message);
            }
        }

        public override void OnNavigatedTo(NavigationContext navigationContext)
        {
            HasSummoner = true;
        }
    }
}

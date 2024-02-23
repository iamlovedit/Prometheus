using Prism.Commands;
using Prism.Regions;
using Prometheus.Core;
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
        public SearchViewModel(IRegionManager regionManager, ISummonerService summonerService) : base(regionManager)
        {
            _summonerService = summonerService;
        }

        private DelegateCommand<string> _searchCommand;
        public DelegateCommand<string> SearchCommand =>
            _searchCommand ?? (_searchCommand = new DelegateCommand<string>(ExecuteSearchCommand));
        async void ExecuteSearchCommand(string name)
        {
            try
            {
                var summoner = await _summonerService.SearchSummonerByName(name);
            }
            catch (HttpRequestException)
            {
                RegionManager.RequestNavigate(RegionNames.SearchContent, RegionNames.UserNotFoundView);
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
    }
}

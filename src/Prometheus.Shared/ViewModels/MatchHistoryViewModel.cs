using Prism.Commands;
using Prism.Ioc;
using Prism.Regions;
using Prometheus.Core;
using Prometheus.Core.Models;
using Prometheus.Core.Mvvm;
using Prometheus.Services.Interfaces.Client;
using System.Collections.Generic;

namespace Prometheus.Shared.ViewModels
{
    public class MatchHistoryViewModel : RegionViewModelBase
    {
        private bool _canEdit;
        private Match _sourceMatch;
        private readonly IGameService _gameService;
        public MatchHistoryViewModel(IRegionManager regionManager, IContainerExtension containerExtension) : base(regionManager)
        {
            _gameService = containerExtension.Resolve<IGameService>();
        }


        private List<Match> _matches;
        public List<Match> Matches
        {
            get { return _matches; }
            set { SetProperty(ref _matches, value); }
        }

        private bool _isLoading;
        public bool IsLoading
        {
            get { return _isLoading; }
            set { SetProperty(ref _isLoading, value); }
        }

        private DelegateCommand _backCommand;
        public DelegateCommand BackCommand =>
            _backCommand ?? (_backCommand = new DelegateCommand(ExecuteBackCommand));
        void ExecuteBackCommand()
        {
            RegionManager.Regions[_canEdit ? RegionNames.SummonerContent : RegionNames.SearchContent].NavigationService.Journal.GoBack();
        }

        public override async void OnNavigatedTo(NavigationContext navigationContext)
        {
            if (navigationContext.Parameters.TryGetValue(ParameterNames.CanEdit, out _canEdit))
            {

            }
            if (navigationContext.Parameters.TryGetValue<Match>(ParameterNames.SelectedMatch, out var match))
            {
                var matchDetail = await _gameService.GetMatchDetailAsync(match.GameId);
            }
        }
    }
}

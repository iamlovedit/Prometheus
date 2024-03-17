using Prism.Commands;
using Prism.Ioc;
using Prism.Regions;
using Prometheus.Core;
using Prometheus.Core.Models;
using Prometheus.Core.Mvvm;
using Prometheus.Services.Interfaces.Client;
using Prometheus.Shared.Models;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Team = Prometheus.Shared.Models.Team;

namespace Prometheus.Shared.ViewModels
{
    public class MatchHistoryViewModel : RegionViewModelBase
    {
        private bool _canEdit;
        private SummonerAccount _summoner;
        private readonly IGameService _gameService;
        private readonly IGameResourceManager _gameResourceManager;
        private readonly ISummonerService _summonerServices;
        public MatchHistoryViewModel(IRegionManager regionManager, IContainerExtension containerExtension) : base(regionManager)
        {
            _gameService = containerExtension.Resolve<IGameService>();
            _gameResourceManager = containerExtension.Resolve<IGameResourceManager>();
            _summonerServices = containerExtension.Resolve<ISummonerService>();
        }

        private List<Match> _matches;
        public List<Match> Matches
        {
            get { return _matches; }
            set { SetProperty(ref _matches, value); }
        }

        private Match _selectedMatch;
        public Match SelectedMatch
        {
            get { return _selectedMatch; }
            set { SetProperty(ref _selectedMatch, value); }
        }

        private MatchDetail _matchDetail;
        public MatchDetail MatchDetail
        {
            get { return _matchDetail; }
            set
            {
                SetProperty(ref _matchDetail, value);
            }
        }

        private bool _isLoading = true;
        public bool IsLoading
        {
            get { return _isLoading; }
            set { SetProperty(ref _isLoading, value); }
        }
        private int _currentPage = 1;
        public int CurrentPage
        {
            get { return _currentPage; }
            set { SetProperty(ref _currentPage, value); }
        }

        private Team _blueTeam;
        public Team BlueTeam
        {
            get { return _blueTeam; }
            set { SetProperty(ref _blueTeam, value); }
        }

        private Team _purpleTeam;
        public Team PurPleTeam
        {
            get { return _purpleTeam; }
            set { SetProperty(ref _purpleTeam, value); }
        }

        private DelegateCommand<Player> _summonerCommand;
        public DelegateCommand<Player> SummonerCommand =>
            _summonerCommand ?? (_summonerCommand = new DelegateCommand<Player>(ExecuteSummonerCommand));
        async void ExecuteSummonerCommand(Player player)
        {
            var summoner = await _summonerServices.SearchSummonerByPuuid(player.Puuid);
            if (summoner != null)
            {
                var parameters = new NavigationParameters()
                {
                    {ParameterNames.Summoner,summoner },
                    {ParameterNames.CanEdit,false }
                };
                RegionManager.RequestNavigate(RegionNames.SummonerContent, RegionNames.SummonerDetailView, parameters);
            }
        }

        private DelegateCommand<object> _matchChangedCommand;
        public DelegateCommand<object> MatchChangedCommand =>
            _matchChangedCommand ?? (_matchChangedCommand = new DelegateCommand<object>(ExecuteMatchChangedCommand));
        async void ExecuteMatchChangedCommand(object obj)
        {
            if (obj is Match match)
            {
                IsLoading = true;
                MatchDetail = await _gameService.GetMatchDetailAsync(match.GameId);
                if (_matchDetail != null)
                {
                    UpdateDetail(_matchDetail);
                }
                IsLoading = false;
            }
        }

        private DelegateCommand _backCommand;
        public DelegateCommand BackCommand =>
            _backCommand ?? (_backCommand = new DelegateCommand(ExecuteBackCommand));
        void ExecuteBackCommand()
        {
            RegionManager.Regions[RegionNames.SummonerContent].NavigationService.Journal.GoBack();
        }

        private DelegateCommand _nextPageCommand;
        public DelegateCommand NextPageCommand =>
            _nextPageCommand ?? (_nextPageCommand = new DelegateCommand(ExecuteNextPageCommand));
        async void ExecuteNextPageCommand()
        {
            IsLoading = true;
            CurrentPage++;
            await GetMatchesAsync(_summoner.Puuid, _currentPage);
            IsLoading = false;
        }

        private DelegateCommand _previousPageCommand;
        public DelegateCommand PreviousPageCommand =>
            _previousPageCommand ?? (_previousPageCommand = new DelegateCommand(ExecutePreviosPageCommand));
        async void ExecutePreviosPageCommand()
        {
            IsLoading = true;
            if (_currentPage == 1)
            {
                return;
            }
            CurrentPage--;
            await GetMatchesAsync(_summoner.Puuid, _currentPage);
            IsLoading = false;
        }

        private async Task GetMatchesAsync(string puuid, int pageIndex)
        {
            var startIndex = pageIndex * 20 - 20;
            var endIndex = pageIndex * 20 - 1;
            Matches = await _summonerServices.GetMatchesAsync(puuid, startIndex, endIndex);
            if (_matches != null)
            {
                _matches.ForEach(async m =>
                {
                    m.Participants[0].ChampionIcon = await _gameResourceManager.GetChampoinIconByIdAsync(m.Participants[0].ChampionId);
                });
                SelectedMatch = Matches.FirstOrDefault();
                MatchDetail = await _gameService.GetMatchDetailAsync(_selectedMatch.GameId);
                if (_matchDetail != null)
                {
                    UpdateDetail(_matchDetail);
                }
            }
        }


        public override async void OnNavigatedTo(NavigationContext navigationContext)
        {
            if (navigationContext.Parameters.TryGetValue(ParameterNames.Matches, out _matches))
            {
                RaisePropertyChanged(nameof(Matches));
            }
            if (navigationContext.Parameters.TryGetValue(ParameterNames.CanEdit, out _canEdit))
            {

            }
            if (navigationContext.Parameters.TryGetValue(ParameterNames.Summoner, out _summoner))
            {

            }

            if (navigationContext.Parameters.TryGetValue<Match>(ParameterNames.SelectedMatch, out var match))
            {
                SelectedMatch = match;
            }
            else
            {
                SelectedMatch = _matches.FirstOrDefault();
            }
            MatchDetail = await _gameService.GetMatchDetailAsync(_selectedMatch.GameId);
            if (_matchDetail != null)
            {
                UpdateDetail(_matchDetail);
            }
            IsLoading = false;
        }

        private async void UpdateDetail(MatchDetail match)
        {
            var bluePlayers = new List<Player>();
            var purplePlayers = new List<Player>();

            for (var i = 0; i < 10; i++)
            {
                var identity = match.ParticipantIdentities[i];
                var participants = match.Participants[i];
                var player = new Player()
                {
                    ChampionIcon = await _gameResourceManager.GetChampoinIconByIdAsync(participants.ChampionId),
                    Puuid = identity.Player.Puuid,
                    Name = identity.Player.GameName,
                    SummonerName = identity.Player.SummonerName,
                    Win = participants.Stats.Win,
                    PerkIcon = await _gameResourceManager.GetPerkIconByIdAsync(participants.Stats.Perk0),
                    Kills = (uint)participants.Stats.Kills,
                    Deaths = (uint)participants.Stats.Deaths,
                    Assists = (uint)participants.Stats.Assists,
                    GoldEarned = (uint)participants.Stats.GoldEarned,
                    Spell1Icon = await _gameResourceManager.GetSpellIconByIdAsync(participants.Spell1Id),
                    Spell2Icon = await _gameResourceManager.GetSpellIconByIdAsync(participants.Spell2Id),
                    ChampLevel = (byte)participants.Stats.ChampLevel,
                    Item0Icon = await _gameResourceManager.GetEquipmentIconByIdAsync(participants.Stats.Item0),
                    Item1Icon = await _gameResourceManager.GetEquipmentIconByIdAsync(participants.Stats.Item1),
                    Item2Icon = await _gameResourceManager.GetEquipmentIconByIdAsync(participants.Stats.Item2),
                    Item3Icon = await _gameResourceManager.GetEquipmentIconByIdAsync(participants.Stats.Item3),
                    Item4Icon = await _gameResourceManager.GetEquipmentIconByIdAsync(participants.Stats.Item4),
                    Item5Icon = await _gameResourceManager.GetEquipmentIconByIdAsync(participants.Stats.Item5),
                    Item6Icon = await _gameResourceManager.GetEquipmentIconByIdAsync(participants.Stats.Item6),
                    TotalDamage = (ulong)participants.Stats.TotalDamageDealtToChampions
                };

                if (i > 4)
                {
                    //purple team
                    purplePlayers.Add(player);
                }
                else
                {
                    bluePlayers.Add(player);
                }
            }
            BlueTeam = new Team
            {
                Players = bluePlayers,
            };

            PurPleTeam = new Team
            {
                Players = purplePlayers,
            };
        }
    }
}

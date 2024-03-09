using Prism.Commands;
using Prism.Ioc;
using Prism.Regions;
using Prometheus.Core;
using Prometheus.Core.Models;
using Prometheus.Core.Mvvm;
using Prometheus.Services.Interfaces.Client;
using System.Collections.Generic;
using System.Linq;

namespace Prometheus.Shared.ViewModels
{
    public class MatchHistoryViewModel : RegionViewModelBase
    {
        private bool _canEdit;
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
                if (value != null)
                {
                    IsLoading = true;
                    //var groups = _matchDetail.Participants.GroupBy(p => p.TeamId).OrderBy(g => g.Key).ToArray();
                    //var blueGroup = groups[0].ToArray();
                    //var purpleGroup = groups[1].ToArray();
                    var bluePlayers = new List<Player>();
                    var purplePlayers = new List<Player>();

                    for (var i = 0; i < 10; i++)
                    {
                        var identity = _matchDetail.ParticipantIdentities[i];
                        var participants = _matchDetail.Participants[i];
                        var player = new Player()
                        {
                            Id = (uint)identity.Player.SummonerId,
                            Name = identity.Player.GameName,
                            Win = participants.Stats.Win,
                            PerkId = (uint)participants.Stats.Perk0,
                            Kills = (uint)participants.Stats.Kills,
                            Deaths = (uint)participants.Stats.Deaths,
                            Assists = (uint)participants.Stats.Assists,
                            GoldEarned = (uint)participants.Stats.GoldEarned,
                            Spell1Id = (uint)participants.Spell1Id,
                            Spell2Id = (uint)participants.Spell2Id,
                            ChampLevel = (byte)participants.Stats.ChampLevel,
                            Item0 = (byte)participants.Stats.Item0,
                            Item1 = (byte)participants.Stats.Item1,
                            Item2 = (byte)participants.Stats.Item2,
                            Item3 = (byte)participants.Stats.Item3,
                            Item4 = (byte)participants.Stats.Item4,
                            Item5 = (byte)participants.Stats.Item5,
                            Item6 = (byte)participants.Stats.Item6,
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
                    IsLoading = false;
                }
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


        private DelegateCommand<object> _matchChangedCommand;
        public DelegateCommand<object> MatchChangedCommand =>
            _matchChangedCommand ?? (_matchChangedCommand = new DelegateCommand<object>(ExecuteMatchChangedCommand));
        async void ExecuteMatchChangedCommand(object obj)
        {
            if (obj is Match match)
            {
                MatchDetail = await _gameService.GetMatchDetailAsync(match.GameId);
            }
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
            if (navigationContext.Parameters.TryGetValue(ParameterNames.Matches, out _matches))
            {
                RaisePropertyChanged(nameof(Matches));
            }
            if (navigationContext.Parameters.TryGetValue(ParameterNames.CanEdit, out _canEdit))
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
        }
    }


    public class Team
    {
        public bool Win
        {
            get
            {
                return Players?.FirstOrDefault()?.Win ?? false;
            }
        }

        private string _kda;
        public string KDA
        {
            get
            {
                if (string.IsNullOrEmpty(_kda))
                {
                    _kda = $"{Players.Sum(p => p.Kills)}/{Players.Sum(p => p.Deaths)}/{Players.Sum(p => p.Assists)}";
                }
                return _kda;
            }

        }

        public uint Gold => (uint)Players.Sum(p => p.GoldEarned);

        public List<Player> Players { get; set; }
    }

    public class Player
    {
        public bool Win { get; set; }

        public uint Id { get; set; }

        public string Name { get; set; }

        public uint PerkId { get; set; }

        public uint Spell1Id { get; set; }

        public uint Spell2Id { get; set; }

        public uint Assists { get; set; }

        public byte ChampLevel { get; set; }

        public uint Deaths { get; set; }

        public uint Kills { get; set; }

        public uint GoldEarned { get; set; }

        public uint Item0 { get; set; }

        public uint Item1 { get; set; }

        public uint Item2 { get; set; }

        public uint Item3 { get; set; }

        public uint Item4 { get; set; }

        public uint Item5 { get; set; }

        public uint Item6 { get; set; }

        public ulong TotalDamage { get; set; }
    }
}

using Prism.Commands;
using Prism.Regions;
using Prometheus.Core;
using Prometheus.Core.Models;
using Prometheus.Core.Mvvm;
using Prometheus.Core.Tasks;
using Prometheus.Services.Interfaces.Client;
using Prometheus.Shared.Models;
using Serilog;
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

        public MatchHistoryViewModel(IRegionManager regionManager, IGameService gameService,
            IGameResourceManager gameResourceManager, ISummonerService summonerServices)
            : base(regionManager)
        {
            _gameService = gameService ?? throw new ArgumentNullException(nameof(gameService));
            _gameResourceManager = gameResourceManager
                ?? throw new ArgumentNullException(nameof(gameResourceManager));
            _summonerServices = summonerServices
                ?? throw new ArgumentNullException(nameof(summonerServices));
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

        private bool _showLoadError;
        public bool ShowLoadError
        {
            get { return _showLoadError; }
            private set { SetProperty(ref _showLoadError, value); }
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
        void ExecuteSummonerCommand(Player player)
        {
            ExecuteSummonerCommandAsync(player).Observe("Loading a summoner from match history");
        }

        private async Task ExecuteSummonerCommandAsync(Player player)
        {
            if (player is null)
            {
                return;
            }

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
        void ExecuteMatchChangedCommand(object obj)
        {
            ExecuteMatchChangedCommandAsync(obj).Observe("Loading match details");
        }

        private async Task ExecuteMatchChangedCommandAsync(object obj)
        {
            if (IsLoading || obj is not Match match)
            {
                return;
            }

            try
            {
                IsLoading = true;
                await LoadMatchDetailAsync(match);
            }
            finally
            {
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

        private async Task ApplyMatchesAsync(List<Match> matches,
            long? selectedGameId = null)
        {
            var iconTasks = matches
                .Where(match => match.Participants?.Count > 0)
                .Select(async match =>
                {
                    var participant = match.Participants[0];
                    participant.ChampionIcon = await _gameResourceManager
                        .GetChampoinIconByIdAsync(participant.ChampionId);
                });
            await Task.WhenAll(iconTasks);

            Matches = matches;
            SelectedMatch = selectedGameId.HasValue
                ? Matches.FirstOrDefault(match => match.GameId == selectedGameId.Value)
                    ?? Matches.FirstOrDefault()
                : Matches.FirstOrDefault();
            await LoadMatchDetailAsync(SelectedMatch);
        }

        private async Task LoadMatchDetailAsync(Match match)
        {
            MatchDetail = null;
            BlueTeam = null;
            PurPleTeam = null;
            if (match is null)
            {
                return;
            }

            MatchDetail = await _gameService.GetMatchDetailAsync(match.GameId);
            if (_matchDetail != null)
            {
                await UpdateDetailAsync(_matchDetail);
            }
        }

        public override void OnNavigatedTo(NavigationContext navigationContext)
        {
            OnNavigatedToAsync(navigationContext).Observe("Loading match history");
        }

        private async Task OnNavigatedToAsync(NavigationContext navigationContext)
        {
            try
            {
                IsLoading = true;
                ShowLoadError = false;
                Matches = [];
                SelectedMatch = null;
                MatchDetail = null;
                BlueTeam = null;
                PurPleTeam = null;

                navigationContext.Parameters.TryGetValue(
                    ParameterNames.CanEdit, out _canEdit);
                navigationContext.Parameters.TryGetValue(
                    ParameterNames.Summoner, out _summoner);

                Match selectedMatch = null;
                if (navigationContext.Parameters.TryGetValue<Match>(
                        ParameterNames.SelectedMatch, out var match))
                {
                    selectedMatch = match;
                }

                if (string.IsNullOrWhiteSpace(_summoner?.Puuid))
                {
                    ShowLoadError = true;
                    Log.Error("Unable to load match history because the summoner is unavailable");
                    return;
                }

                var result = await _summonerServices.GetMatchHistoryAsync(_summoner.Puuid);
                if (result?.Succeeded != true)
                {
                    ShowLoadError = true;
                    Log.Error("Unable to load match history: {Reason}",
                        result?.Error ?? "No result was returned.");
                    return;
                }

                var matches = result.Matches?.ToList() ?? [];
                if (matches.Count == 0)
                {
                    SelectedMatch = null;
                    await LoadMatchDetailAsync(null);
                    return;
                }

                await ApplyMatchesAsync(matches, selectedMatch?.GameId);
            }
            finally
            {
                IsLoading = false;
            }
        }

        private async Task UpdateDetailAsync(MatchDetail match)
        {
            var bluePlayers = new List<Player>();
            var purplePlayers = new List<Player>();

            var playerCount = Math.Min(match.ParticipantIdentities?.Count ?? 0,
                match.Participants?.Count ?? 0);
            for (var i = 0; i < playerCount; i++)
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

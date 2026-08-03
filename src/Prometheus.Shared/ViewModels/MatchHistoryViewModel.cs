using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Prism.Commands;
using Prism.Events;
using Prism.Regions;
using Prometheus.Core;
using Prometheus.Core.Events;
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
        private string _hostRegionName = RegionNames.SummonerContent;
        private readonly IGameService _gameService;
        private readonly IGameResourceManager _gameResourceManager;
        private readonly ISummonerService _summonerServices;
        private readonly IEventAggregator _eventAggregator;
        private CancellationTokenSource _summonerLoadCts;
        private int _summonerLoadVersion;

        public MatchHistoryViewModel(IRegionManager regionManager,
            IGameService gameService,
            IGameResourceManager gameResourceManager,
            ISummonerService summonerServices,
            IEventAggregator eventAggregator)
            : base(regionManager)
        {
            _gameService = gameService ?? throw new ArgumentNullException(nameof(gameService));
            _gameResourceManager = gameResourceManager
                ?? throw new ArgumentNullException(nameof(gameResourceManager));
            _summonerServices = summonerServices
                ?? throw new ArgumentNullException(nameof(summonerServices));
            _eventAggregator = eventAggregator
                ?? throw new ArgumentNullException(nameof(eventAggregator));
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
            set { SetProperty(ref _matchDetail, value); }
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

        private bool _showPageHeader = true;
        public bool ShowPageHeader
        {
            get { return _showPageHeader; }
            private set { SetProperty(ref _showPageHeader, value); }
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

        private bool _isPreviewOpen;
        public bool IsPreviewOpen
        {
            get { return _isPreviewOpen; }
            private set { SetProperty(ref _isPreviewOpen, value); }
        }

        private bool _isPreviewLoading;
        public bool IsPreviewLoading
        {
            get { return _isPreviewLoading; }
            private set { SetProperty(ref _isPreviewLoading, value); }
        }

        private bool _showPreviewError;
        public bool ShowPreviewError
        {
            get { return _showPreviewError; }
            private set { SetProperty(ref _showPreviewError, value); }
        }

        private SummonerQuickPreview _preview;
        public SummonerQuickPreview Preview
        {
            get { return _preview; }
            private set
            {
                if (SetProperty(ref _preview, value))
                {
                    _viewFullRecordCommand?.RaiseCanExecuteChanged();
                }
            }
        }

        private DelegateCommand<Player> _summonerCommand;
        public DelegateCommand<Player> SummonerCommand =>
            _summonerCommand ??= new DelegateCommand<Player>(ExecuteSummonerCommand);

        private void ExecuteSummonerCommand(Player player)
        {
            ExecuteSummonerCommandAsync(player)
                .Observe("Loading a summoner from match history");
        }

        private async Task ExecuteSummonerCommandAsync(Player player)
        {
            if (player is null || string.IsNullOrWhiteSpace(player.Puuid) ||
                IsSamePuuid(player.Puuid, _summoner?.Puuid))
            {
                return;
            }

            var cancellationTokenSource = new CancellationTokenSource();
            var version = Interlocked.Increment(ref _summonerLoadVersion);
            Cancel(Interlocked.Exchange(ref _summonerLoadCts,
                cancellationTokenSource));
            var cancellationToken = cancellationTokenSource.Token;

            try
            {
                if (string.Equals(_hostRegionName, RegionNames.SearchContent,
                        StringComparison.Ordinal))
                {
                    var target = await _summonerServices.SearchSummonerByPuuid(
                        player.Puuid, cancellationToken);
                    cancellationToken.ThrowIfCancellationRequested();
                    if (version == Volatile.Read(ref _summonerLoadVersion) &&
                        target is not null)
                    {
                        NavigateToSearchResult(target);
                    }

                    return;
                }

                Preview = null;
                ShowPreviewError = false;
                IsPreviewLoading = true;
                IsPreviewOpen = true;

                var summoner = await _summonerServices.SearchSummonerByPuuid(
                    player.Puuid, cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                if (summoner is null)
                {
                    if (version == Volatile.Read(ref _summonerLoadVersion))
                    {
                        ShowPreviewError = true;
                    }

                    return;
                }

                var profileIconTask = LoadPreviewProfileIconAsync(
                    summoner.ProfileIconId, cancellationToken);
                var ranksTask = LoadPreviewRanksAsync(summoner.Puuid,
                    cancellationToken);
                var matchesTask = LoadPreviewMatchesAsync(summoner.Puuid,
                    cancellationToken);
                await Task.WhenAll(profileIconTask, ranksTask, matchesTask);
                cancellationToken.ThrowIfCancellationRequested();
                if (version != Volatile.Read(ref _summonerLoadVersion))
                {
                    return;
                }

                var ranks = await ranksTask;
                Preview = BuildPreview(
                    summoner,
                    await profileIconTask,
                    ranks.Solo,
                    ranks.Flex,
                    await matchesTask);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
            }
            catch (Exception exception)
            {
                if (version == Volatile.Read(ref _summonerLoadVersion))
                {
                    ShowPreviewError = true;
                }

                Log.Warning(exception, "Unable to load summoner quick preview");
            }
            finally
            {
                if (version == Volatile.Read(ref _summonerLoadVersion))
                {
                    IsPreviewLoading = false;
                }

                Interlocked.CompareExchange(ref _summonerLoadCts, null,
                    cancellationTokenSource);
                cancellationTokenSource.Dispose();
            }
        }

        private void NavigateToSearchResult(SummonerAccount summoner)
        {
            var parameters = new NavigationParameters
            {
                { ParameterNames.Summoner, summoner },
                { ParameterNames.CanEdit, false },
                { ParameterNames.HostRegionName, RegionNames.SearchContent },
                { ParameterNames.ShowPageHeader, false }
            };
            RegionManager.RequestNavigate(RegionNames.SearchContent,
                RegionNames.SummonerDetailView, parameters);
        }

        private async Task<string> LoadPreviewProfileIconAsync(int profileIconId,
            CancellationToken cancellationToken)
        {
            try
            {
                var icon = await _gameResourceManager
                    .GetProfileIconByIdAsync(profileIconId);
                cancellationToken.ThrowIfCancellationRequested();
                return icon;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                Log.Warning(exception, "Unable to load quick-preview profile icon");
                return null;
            }
        }

        private async Task<(Rank Solo, Rank Flex)> LoadPreviewRanksAsync(
            string puuid, CancellationToken cancellationToken)
        {
            var solo = CreateUnrankedRank(QueueType.RANKED_SOLO_5x5);
            var flex = CreateUnrankedRank(QueueType.RANKED_FLEX_SR);

            try
            {
                var rankJson = await _summonerServices.GetRankStatsByPuuid(puuid,
                    cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                if (!string.IsNullOrWhiteSpace(rankJson))
                {
                    var queueMap = JObject.Parse(rankJson)["queueMap"];
                    solo = queueMap?["RANKED_SOLO_5x5"]?.ToObject<Rank>() ?? solo;
                    flex = queueMap?["RANKED_FLEX_SR"]?.ToObject<Rank>() ?? flex;
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (JsonException exception)
            {
                Log.Warning(exception, "Unable to parse quick-preview ranked stats");
            }
            catch (Exception exception)
            {
                Log.Warning(exception, "Unable to load quick-preview ranked stats");
            }

            return (solo, flex);
        }

        private async Task<IReadOnlyList<Match>> LoadPreviewMatchesAsync(
            string puuid, CancellationToken cancellationToken)
        {
            var result = await _summonerServices.GetMatchHistoryAsync(puuid,
                cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (result?.Succeeded != true)
            {
                throw new InvalidOperationException(
                    "Quick-preview match history is unavailable.");
            }

            return result.Matches?.Take(20).ToList() ?? [];
        }

        private static SummonerQuickPreview BuildPreview(SummonerAccount summoner,
            string profileIcon, Rank solo, Rank flex,
            IReadOnlyList<Match> sourceMatches)
        {
            var matches = (sourceMatches ?? Array.Empty<Match>())
                .Where(match => match?.Participants?.Count > 0 &&
                    match.Participants[0]?.Stats is not null)
                .Take(20)
                .ToList();
            var wins = matches.Count(match => match.Participants[0].Stats.Win);
            var losses = matches.Count - wins;
            var kills = matches.Sum(match => match.Participants[0].Stats.Kills);
            var deaths = matches.Sum(match => match.Participants[0].Stats.Deaths);
            var assists = matches.Sum(match => match.Participants[0].Stats.Assists);
            var winRate = matches.Count == 0
                ? 0d
                : wins * 100d / matches.Count;
            var kda = (kills + assists) / (double)Math.Max(1, deaths);
            var results = matches
                .AsEnumerable()
                .Reverse()
                .Select((match, index) => new RecentMatchResult
                {
                    Index = index + 1,
                    IsWin = match.Participants[0].Stats.Win
                })
                .ToList();

            return new SummonerQuickPreview
            {
                Summoner = summoner,
                ProfileIcon = profileIcon,
                Solo = solo,
                Flex = flex,
                MatchCount = matches.Count,
                Wins = wins,
                Losses = losses,
                WinRate = $"{Math.Round(winRate, 1, MidpointRounding.AwayFromZero):0.#}%",
                Kda = kda.ToString("0.00"),
                Results = results
            };
        }

        private static Rank CreateUnrankedRank(QueueType queueType)
        {
            return new Rank
            {
                QueueType = queueType,
                Tier = Tier.UNRANKED
            };
        }

        private DelegateCommand<object> _matchChangedCommand;
        public DelegateCommand<object> MatchChangedCommand =>
            _matchChangedCommand ??= new DelegateCommand<object>(ExecuteMatchChangedCommand);

        private void ExecuteMatchChangedCommand(object obj)
        {
            ExecuteMatchChangedCommandAsync(obj).Observe("Loading match details");
        }

        private async Task ExecuteMatchChangedCommandAsync(object obj)
        {
            if (IsLoading || obj is not Match match)
            {
                return;
            }

            ClosePreview();
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
            _backCommand ??= new DelegateCommand(ExecuteBackCommand);

        private void ExecuteBackCommand()
        {
            RegionManager.Regions[_hostRegionName].NavigationService.Journal.GoBack();
        }

        private DelegateCommand _closePreviewCommand;
        public DelegateCommand ClosePreviewCommand =>
            _closePreviewCommand ??= new DelegateCommand(ClosePreview);

        private void ClosePreview()
        {
            CancelPendingSummonerLoad();
            IsPreviewOpen = false;
            IsPreviewLoading = false;
            ShowPreviewError = false;
            Preview = null;
        }

        private DelegateCommand _viewFullRecordCommand;
        public DelegateCommand ViewFullRecordCommand =>
            _viewFullRecordCommand ??= new DelegateCommand(ExecuteViewFullRecordCommand,
                () => Preview?.Summoner is not null);

        private void ExecuteViewFullRecordCommand()
        {
            var summoner = Preview?.Summoner;
            if (summoner is null)
            {
                return;
            }

            ClosePreview();
            _eventAggregator.GetEvent<SearchSummonerEvent>().Publish(summoner);
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
            _hostRegionName = navigationContext.Parameters.TryGetValue<string>(
                    ParameterNames.HostRegionName, out var hostRegionName) &&
                !string.IsNullOrWhiteSpace(hostRegionName)
                ? hostRegionName
                : RegionNames.SummonerContent;
            ShowPageHeader = !navigationContext.Parameters.TryGetValue<bool>(
                    ParameterNames.ShowPageHeader, out var showPageHeader) ||
                showPageHeader;
            ClosePreview();
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
                    Log.Error("Unable to load match history");
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
                var player = new Player
                {
                    ChampionIcon = await _gameResourceManager
                        .GetChampoinIconByIdAsync(participants.ChampionId),
                    Puuid = identity.Player.Puuid,
                    Name = identity.Player.GameName,
                    SummonerName = identity.Player.SummonerName,
                    Win = participants.Stats.Win,
                    PerkIcon = await _gameResourceManager
                        .GetPerkIconByIdAsync(participants.Stats.Perk0),
                    Kills = (uint)participants.Stats.Kills,
                    Deaths = (uint)participants.Stats.Deaths,
                    Assists = (uint)participants.Stats.Assists,
                    GoldEarned = (uint)participants.Stats.GoldEarned,
                    Spell1Icon = await _gameResourceManager
                        .GetSpellIconByIdAsync(participants.Spell1Id),
                    Spell2Icon = await _gameResourceManager
                        .GetSpellIconByIdAsync(participants.Spell2Id),
                    ChampLevel = (byte)participants.Stats.ChampLevel,
                    Item0Icon = await _gameResourceManager
                        .GetEquipmentIconByIdAsync(participants.Stats.Item0),
                    Item1Icon = await _gameResourceManager
                        .GetEquipmentIconByIdAsync(participants.Stats.Item1),
                    Item2Icon = await _gameResourceManager
                        .GetEquipmentIconByIdAsync(participants.Stats.Item2),
                    Item3Icon = await _gameResourceManager
                        .GetEquipmentIconByIdAsync(participants.Stats.Item3),
                    Item4Icon = await _gameResourceManager
                        .GetEquipmentIconByIdAsync(participants.Stats.Item4),
                    Item5Icon = await _gameResourceManager
                        .GetEquipmentIconByIdAsync(participants.Stats.Item5),
                    Item6Icon = await _gameResourceManager
                        .GetEquipmentIconByIdAsync(participants.Stats.Item6),
                    TotalDamage = (ulong)participants.Stats.TotalDamageDealtToChampions
                };

                if (i > 4)
                {
                    purplePlayers.Add(player);
                }
                else
                {
                    bluePlayers.Add(player);
                }
            }

            BlueTeam = new Team
            {
                Players = bluePlayers
            };
            PurPleTeam = new Team
            {
                Players = purplePlayers
            };
        }

        public override void OnNavigatedFrom(NavigationContext navigationContext)
        {
            ClosePreview();
        }

        public override void Destroy()
        {
            ClosePreview();
            base.Destroy();
        }

        private void CancelPendingSummonerLoad()
        {
            Interlocked.Increment(ref _summonerLoadVersion);
            Cancel(Interlocked.Exchange(ref _summonerLoadCts, null));
        }

        private static void Cancel(CancellationTokenSource cancellationTokenSource)
        {
            try
            {
                cancellationTokenSource?.Cancel();
            }
            catch (ObjectDisposedException)
            {
            }
        }

        private static bool IsSamePuuid(string left, string right)
        {
            return !string.IsNullOrWhiteSpace(left) &&
                   !string.IsNullOrWhiteSpace(right) &&
                   string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
        }
    }
}

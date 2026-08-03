using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Prism.Commands;
using Prism.Ioc;
using Prism.Regions;
using Prism.Services.Dialogs;
using Prometheus.Core;
using Prometheus.Core.Models;
using Prometheus.Core.Mvvm;
using Prometheus.Core.Tasks;
using Prometheus.Services.Interfaces.Client;
using Serilog;
using System.Windows;

namespace Prometheus.Shared.ViewModels
{
    public class SummonerDetailViewModel : RegionViewModelBase
    {
        private readonly ISummonerService _summonerService;
        private readonly IGameResourceManager _gameResourceManager;
        private readonly IDialogService _dialogService;
        private readonly IResourceService _resourceService;
        private CancellationTokenSource _loadCts;
        private int _loadVersion;
        //private readonly static Dictionary<Tier, string> _tierIconReosourceMap = new()
        //{
        //    { Tier.UNRANKED,"Career.Rank.Tier.Unranked"},
        //    { Tier.IRON,"Career.Rank.Tier.Iron"},
        //    { Tier.BRONZE,"Career.Rank.Tier.Bronze"},
        //    { Tier.GOLD,"Career.Rank.Tier.Gold"},
        //    { Tier.PLATINUM,"Career.Rank.Tier.Platinum"},
        //    { Tier.EMERALD,"Career.Rank.Tier.Emerald"},
        //    { Tier.DIAMOND,"Career.Rank.Tier.Diamond"},
        //    { Tier.MASTER,"Career.Rank.Tier.Master"},
        //    { Tier.GRANDMASTER,"Career.Rank.Tier.Grandmaster"},
        //    { Tier.CHALLENGER,"Career.Rank.Tier.Challenger"},
        //};
        public SummonerDetailViewModel(IRegionManager regionManager, IContainerExtension containerExtension) : base(regionManager)
        {
            _resourceService = containerExtension.Resolve<IResourceService>();
            //_eventAggregator.GetEvent<LanguageSwitchedEvent>().Subscribe(() =>
            //{
            //    FlexTier = _resourceService.FindResource<string>(_tierIconReosourceMap[_flex.Tier]);
            //    SoloTier = _resourceService.FindResource<string>(_tierIconReosourceMap[_solo.Tier]);
            //});
            _summonerService = containerExtension.Resolve<ISummonerService>();
            _gameResourceManager = containerExtension.Resolve<IGameResourceManager>();
            _dialogService = containerExtension.Resolve<IDialogService>();
        }
        public override void OnNavigatedTo(NavigationContext navigationContext)
        {
            var cancellationTokenSource = new CancellationTokenSource();
            var version = Interlocked.Increment(ref _loadVersion);
            var previousLoad = Interlocked.Exchange(ref _loadCts, cancellationTokenSource);
            Cancel(previousLoad);
            OnNavigatedToAsync(navigationContext, version, cancellationTokenSource)
                .Observe("Loading summoner career data");
        }

        private async Task OnNavigatedToAsync(NavigationContext navigationContext,
            int version, CancellationTokenSource cancellationTokenSource)
        {
            IsLoading = true;
            var cancellationToken = cancellationTokenSource.Token;

            try
            {
                ResetPresentation();
                var canEdit = navigationContext.Parameters.TryGetValue<bool>(
                    ParameterNames.CanEdit, out var editable) && editable;
                CanModify = canEdit;
                if (navigationContext.Parameters.TryGetValue<SummonerAccount>(
                        ParameterNames.Summoner, out var summoner) && summoner is not null)
                {
                    Summoner = summoner;
                    IsPublic = summoner.Privacy == "PUBLIC";
                    await LoadBackgroundAsync(summoner, canEdit, version,
                        cancellationToken);
                    cancellationToken.ThrowIfCancellationRequested();
                    await LoadProfileIconAsync(summoner.ProfileIconId, version,
                        cancellationToken);
                    cancellationToken.ThrowIfCancellationRequested();
                    await LoadRanksAsync(summoner.Puuid, version, cancellationToken);
                    cancellationToken.ThrowIfCancellationRequested();
                    await LoadRecentMatchesAsync(summoner.Puuid, version,
                        cancellationToken);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
            }
            finally
            {
                if (version == Volatile.Read(ref _loadVersion))
                {
                    IsLoading = false;
                }

                Interlocked.CompareExchange(ref _loadCts, null,
                    cancellationTokenSource);
                cancellationTokenSource.Dispose();
            }
        }

        private void ResetPresentation()
        {
            BackgroundSkin = null;
            ProfileIcon = null;
            RecentMatches = null;
            Wins = 0;
            Losses = 0;
            KDA = string.Empty;
            ApplyRanks(
                CreateUnrankedRank(QueueType.RANKED_SOLO_5x5),
                CreateUnrankedRank(QueueType.RANKED_FLEX_SR));
        }

        private async Task LoadBackgroundAsync(SummonerAccount summoner, bool canEdit,
            int version, CancellationToken cancellationToken)
        {
            var skinId = 0;
            try
            {
                var json = canEdit
                    ? await _gameResourceManager.GetBackgroundSkinId()
                    : await _summonerService.GetBackdorpByIdAsync(summoner.SummonerId,
                        cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                skinId = ParseBackgroundSkinId(json);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                Log.Warning(exception, "Unable to load summoner backdrop metadata");
            }

            try
            {
                var backgroundSkin = await _gameResourceManager
                    .GetBackgroundSkinByIdAsync(skinId);
                cancellationToken.ThrowIfCancellationRequested();
                if (version == Volatile.Read(ref _loadVersion))
                {
                    BackgroundSkin = backgroundSkin;
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                Log.Warning(exception, "Unable to load summoner backdrop image");
            }
        }

        private async Task LoadProfileIconAsync(int profileIconId, int version,
            CancellationToken cancellationToken)
        {
            try
            {
                var profileIcon = await _gameResourceManager
                    .GetProfileIconByIdAsync(profileIconId);
                cancellationToken.ThrowIfCancellationRequested();
                if (version == Volatile.Read(ref _loadVersion))
                {
                    ProfileIcon = profileIcon;
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                Log.Warning(exception, "Unable to load summoner profile icon");
            }
        }

        private async Task LoadRanksAsync(string puuid, int version,
            CancellationToken cancellationToken)
        {
            var solo = CreateUnrankedRank(QueueType.RANKED_SOLO_5x5);
            var flex = CreateUnrankedRank(QueueType.RANKED_FLEX_SR);

            try
            {
                var rankJson = await _summonerService.GetRankStatsByPuuid(puuid,
                    cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                if (!string.IsNullOrWhiteSpace(rankJson))
                {
                    var queueMap = JObject.Parse(rankJson)["queueMap"];
                    solo = queueMap?["RANKED_SOLO_5x5"]?.ToObject<Rank>() ?? solo;
                    flex = queueMap?["RANKED_FLEX_SR"]?.ToObject<Rank>() ?? flex;
                }
            }
            catch (JsonException exception)
            {
                Log.Warning(exception, "Unable to parse summoner ranked stats");
            }
            catch (HttpRequestException exception)
            {
                Log.Warning(exception, "Unable to load summoner ranked stats");
            }

            if (version == Volatile.Read(ref _loadVersion))
            {
                ApplyRanks(solo, flex);
            }
        }

        private async Task LoadRecentMatchesAsync(string puuid, int version,
            CancellationToken cancellationToken)
        {
            var matches = new List<Match>();
            try
            {
                var matchResult = await _summonerService.GetMatchHistoryAsync(puuid,
                    cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                if (matchResult?.Succeeded == true)
                {
                    matches = matchResult.Matches?.ToList() ?? [];
                }

                await Task.WhenAll(matches.Select(async match =>
                    match.Participants[0].ChampionIcon = await _gameResourceManager
                        .GetChampoinIconByIdAsync(match.Participants[0].ChampionId)));
                cancellationToken.ThrowIfCancellationRequested();
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                Log.Warning(exception, "Unable to load summoner recent matches");
                matches = [];
            }

            if (version != Volatile.Read(ref _loadVersion))
            {
                return;
            }

            Wins = matches.Count(match => match.Participants[0].Stats.Win);
            Losses = matches.Count - Wins;
            var killed = matches.Sum(match => match.Participants[0].Stats.Kills);
            var deaths = matches.Sum(match => match.Participants[0].Stats.Deaths);
            var assists = matches.Sum(match => match.Participants[0].Stats.Assists);
            KDA = $"{killed}/{deaths}/{assists}";
            RecentMatches = CollectionViewSource.GetDefaultView(matches) as ListCollectionView;
        }

        private void ApplyRanks(Rank solo, Rank flex)
        {
            Solo = solo;
            Flex = flex;
            Ranks = [Solo, Flex];
            RaisePropertyChanged(nameof(Ranks));
            SoloIcon = _resourceService.GetTierIconResourceUri(
                Solo.Tier.ToString().ToLowerInvariant());
            FlexIcon = _resourceService.GetTierIconResourceUri(
                Flex.Tier.ToString().ToLowerInvariant());
        }

        private static Rank CreateUnrankedRank(QueueType queueType)
        {
            return new Rank
            {
                QueueType = queueType,
                Tier = Tier.UNRANKED
            };
        }

        private static int ParseBackgroundSkinId(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                return 0;
            }

            var backdrop = JObject.Parse(json);
            var skinId = backdrop["backgroundSkinId"]?.ToObject<int?>() ?? 0;
            if (skinId > 0)
            {
                return skinId;
            }

            var imagePath = backdrop["backdropImage"]?.ToString();
            if (string.IsNullOrWhiteSpace(imagePath))
            {
                return 0;
            }

            var pathWithoutQuery = imagePath.Split(['?', '#'], 2)[0];
            var fileName = Path.GetFileNameWithoutExtension(pathWithoutQuery);
            if (int.TryParse(fileName, out skinId))
            {
                return BuildSkinId(backdrop, skinId);
            }

            var separatorIndex = fileName.LastIndexOf('_');
            if (separatorIndex < 0 || separatorIndex == fileName.Length - 1 ||
                !int.TryParse(fileName[(separatorIndex + 1)..], out var skinNumber))
            {
                return 0;
            }

            return BuildSkinId(backdrop, skinNumber);
        }

        private static int BuildSkinId(JObject backdrop, int skinNumberOrId)
        {
            if (skinNumberOrId >= 1000)
            {
                return skinNumberOrId;
            }

            var championId = backdrop["championId"]?.ToObject<int?>() ?? 0;
            return championId > 0 && skinNumberOrId >= 0
                ? championId * 1000 + skinNumberOrId
                : 0;
        }

        public override void OnNavigatedFrom(NavigationContext navigationContext)
        {
            CancelPendingLoad();
            RecentMatches = null;
            SelectedMatchTypeIndex = 0;
            IsLoading = false;
        }

        public override void Destroy()
        {
            CancelPendingLoad();
            base.Destroy();
        }

        private void CancelPendingLoad()
        {
            Interlocked.Increment(ref _loadVersion);
            Cancel(Interlocked.Exchange(ref _loadCts, null));
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

        public Rank[] Ranks { get; set; }

        private int _wins;
        public int Wins
        {
            get { return _wins; }
            set { SetProperty(ref _wins, value); }
        }

        private int _losses;
        public int Losses
        {
            get { return _losses; }
            set { SetProperty(ref _losses, value); }
        }

        private bool _isLoading = true;
        public bool IsLoading
        {
            get { return _isLoading; }
            set { SetProperty(ref _isLoading, value); }
        }

        private ListCollectionView _recentMatches;
        public ListCollectionView RecentMatches
        {
            get { return _recentMatches; }
            set
            {
                SetProperty(ref _recentMatches, value);
            }
        }

        private string _kda;
        public string KDA
        {
            get { return _kda; }
            set { SetProperty(ref _kda, value); }
        }

        private Rank _flex;
        public Rank Flex
        {
            get { return _flex; }
            set { SetProperty(ref _flex, value); }
        }

        private Rank _solo;
        public Rank Solo
        {
            get { return _solo; }
            set { SetProperty(ref _solo, value); }
        }

        private SummonerAccount _summoner;
        public SummonerAccount Summoner
        {
            get { return _summoner; }
            set
            {
                SetProperty(ref _summoner, value);
            }
        }

        private string _backgroundSkin;
        public string BackgroundSkin
        {
            get { return _backgroundSkin; }
            set { SetProperty(ref _backgroundSkin, value); }
        }

        private string _profileIcon;
        public string ProfileIcon
        {
            get { return _profileIcon; }
            set { SetProperty(ref _profileIcon, value); }
        }

        private string _soloIcon;
        public string SoloIcon
        {
            get { return _soloIcon; }
            set { SetProperty(ref _soloIcon, value); }
        }

        private string _flexIcon;
        public string FlexIcon
        {
            get { return _flexIcon; }
            set { SetProperty(ref _flexIcon, value); }
        }

        private bool _canModify;
        public bool CanModify
        {
            get { return _canModify; }
            set { SetProperty(ref _canModify, value); }
        }

        private bool _isPublic = true;
        public bool IsPublic
        {
            get { return _isPublic; }
            set { SetProperty(ref _isPublic, value); }
        }

        private Match _selectedMatch;
        public Match SelectedMatch
        {
            get { return _selectedMatch; }
            set { SetProperty(ref _selectedMatch, value); }
        }

        private int _selectedMatchTypeIndex;
        public int SelectedMatchTypeIndex
        {
            get { return _selectedMatchTypeIndex; }
            set { SetProperty(ref _selectedMatchTypeIndex, value); }
        }

        private DelegateCommand _matchTypeChangedCommand;
        public DelegateCommand MatchTypeChangedCommand =>
            _matchTypeChangedCommand ?? (_matchTypeChangedCommand = new DelegateCommand(ExecuteMatchTypeChangedCommand));
        void ExecuteMatchTypeChangedCommand()
        {
            //TODO:
            switch (_selectedMatchTypeIndex)
            {
                case 1:
                    _recentMatches.Filter = (@object) =>
                    {
                        if (@object is Match match)
                        {
                            return match.GameMode == "ARAM";
                        }
                        return true;
                    };
                    break;
                case 2:
                    break;
                case 3:
                    break;
                case 4:
                    break;
                default:
                    if (_recentMatches != null)
                    {
                        _recentMatches.Filter = null;
                    }
                    break;
            }
        }

        private DelegateCommand _moreMatchCommand;
        public DelegateCommand MoreMatchCommand =>
            _moreMatchCommand ?? (_moreMatchCommand = new DelegateCommand(ExecuteMoreMatchCommand));
        void ExecuteMoreMatchCommand()
        {
            var parameters = new NavigationParameters()
            {
                {ParameterNames.CanEdit,CanModify },
                {ParameterNames.Summoner,_summoner },
            };
            RegionManager.RequestNavigate(RegionNames.SummonerContent, RegionNames.MatchHistoryView, parameters);
        }

        private DelegateCommand<Match> _matchDetailCommand;
        public DelegateCommand<Match> MatchDetailCommand =>
            _matchDetailCommand ?? (_matchDetailCommand = new DelegateCommand<Match>(ExecuteMatchDetailCommand));
        void ExecuteMatchDetailCommand(Match match)
        {
            var parameters = new NavigationParameters()
            {
                {ParameterNames.CanEdit,CanModify },
                {ParameterNames.SelectedMatch,match},
                {ParameterNames.Summoner,_summoner },
            };
            RegionManager.RequestNavigate(CanModify ? RegionNames.SummonerContent : RegionNames.SummonerContent, RegionNames.MatchHistoryView, parameters);
        }

        private DelegateCommand _modifyCommand;
        public DelegateCommand ModifyCommand =>
            _modifyCommand ?? (_modifyCommand = new DelegateCommand(ExecuteModifyCommand));
        void ExecuteModifyCommand()
        {
            _dialogService.ShowDialog(RegionNames.SelectBackgroundDialog, dialogResult =>
            {
                if (dialogResult.Result == ButtonResult.OK)
                {
                    if (dialogResult.Parameters.TryGetValue<string>(ParameterNames.SelectedSkinUri, out var uri))
                    {
                        BackgroundSkin = uri;
                    }
                }
            });
        }

        private DelegateCommand _backMeCommand;
        public DelegateCommand BackMeCommand =>
            _backMeCommand ?? (_backMeCommand = new DelegateCommand(ExecuteBackMeCommand));
        async void ExecuteBackMeCommand()
        {
            var summoner = await _summonerService.GetCurrentSummoner();
            var parameters = new NavigationParameters()
            {
                {ParameterNames.CanEdit,true},
                {ParameterNames.Summoner,summoner},
            };

            RegionManager.RequestNavigate(RegionNames.SummonerContent, RegionNames.SummonerDetailView, parameters);
        }

        private DelegateCommand _refreshCommand;
        public DelegateCommand RefreshCommand =>
            _refreshCommand ?? (_refreshCommand = new DelegateCommand(ExecuteRefreshCommand));
        void ExecuteRefreshCommand()
        {
            //TODO:
        }



        private DelegateCommand _copyCommand;
        public DelegateCommand CopyCommand =>
            _copyCommand ?? (_copyCommand = new DelegateCommand(ExecuteCopyCommand));
        void ExecuteCopyCommand()
        {
            Clipboard.SetText(_summoner.FullName);
        }
    }
}

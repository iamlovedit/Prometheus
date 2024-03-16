using Newtonsoft.Json.Linq;
using Prism.Commands;
using Prism.Events;
using Prism.Ioc;
using Prism.Regions;
using Prism.Services.Dialogs;
using Prometheus.Core;
using Prometheus.Core.Events;
using Prometheus.Core.Models;
using Prometheus.Core.Mvvm;
using Prometheus.Services.Interfaces.Client;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Data;

namespace Prometheus.Shared.ViewModels
{
    public class SummonerDetailViewModel : RegionViewModelBase
    {
        private readonly ISummonerService _summonerService;
        private readonly IGameResourceManager _gameResourceManager;
        private readonly IEventAggregator _eventAggregator;
        private readonly IDialogService _dialogService;
        private readonly IResourceService _resourceService;
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
        public SummonerDetailViewModel(IRegionManager regionManager, IEventAggregator eventAggregator, IContainerExtension containerExtension) : base(regionManager)
        {
            _eventAggregator = eventAggregator;
            _eventAggregator.GetEvent<ConnectLCUEvent>().Subscribe(OnConnectHandler);
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
        private void OnConnectHandler(bool isConnected)
        {

            if (isConnected)
            {
                //TODO:UpDate Summoner 
            }
            else
            {
                //TODO:Clear Summoner
                RegionManager.RequestNavigate(RegionNames.ContentRegion, RegionNames.HomeView);
            }
        }

        public override async void OnNavigatedTo(NavigationContext navigationContext)
        {
            if (navigationContext.Parameters.TryGetValue<bool>(ParameterNames.CanEdit, out var canEdit))
            {
                CanModify = canEdit;
            }
            if (navigationContext.Parameters.TryGetValue<SummonerAccount>(ParameterNames.Summoner, out var summoner))
            {
                Summoner = summoner;
                IsPublic = summoner.Privacy == "PUBLIC";
                var skinId = 0;
                if (canEdit)
                {
                    var jsonValue = await _gameResourceManager.GetBackgroundSkinId();
                    if (!string.IsNullOrEmpty(jsonValue))
                    {
                        skinId = JObject.Parse(jsonValue)["backgroundSkinId"].ToObject<int>();
                    }
                }
                else
                {
                    var jsonValue = await _summonerService.GetBackdorpByIdAsync(summoner.SummonerId);
                    if (!string.IsNullOrEmpty(jsonValue))
                    {
                        var uri = JObject.Parse(jsonValue)["backdropImage"].ToString();
                        if (int.TryParse(Path.GetFileNameWithoutExtension(uri), out skinId))
                        {
                        }
                    }
                }
                BackgroundSkin = await _gameResourceManager.GetBackgroundSkinByIdAsync(skinId);
                ProfileIcon = await _gameResourceManager.GetProfileIconByIdAsync(_summoner.ProfileIconId);
                var rankJson = await _summonerService.GetRankStatsByPuuid(_summoner.Puuid);
                if (!string.IsNullOrEmpty(rankJson))
                {
                    var jObject = JObject.Parse(rankJson);
                    Flex = jObject["queueMap"]["RANKED_FLEX_SR"].ToObject<Rank>();
                    Solo = jObject["queueMap"]["RANKED_SOLO_5x5"].ToObject<Rank>();
                    Ranks = [Solo, Flex];
                    RaisePropertyChanged(nameof(Ranks));
                    SoloIcon = _resourceService.GetTierIconResourceUri(Solo.Tier.ToString().ToLower());
                    FlexIcon = _resourceService.GetTierIconResourceUri(Flex.Tier.ToString().ToLower());

                    var masteries = await _summonerService.GetChampionMasteriesAsync(_summoner.Puuid, 3);
                    masteries.ForEach(async m =>
                    {
                        m.ChampionIcon = await _gameResourceManager.GetChampoinIconByIdAsync(m.ChampionId);
                    });
                    Mastery1 = masteries[0];
                    Mastery2 = masteries[1];
                    Mastery3 = masteries[2];
                }
                var matches = await _summonerService.GetMatchsAsync(_summoner.Puuid, 0, 19);
                if (matches != null)
                {
                    Wins = matches.Where(m => m.Participants[0].Stats.Win).Count();
                    var killed = matches.Sum(m => m.Participants[0].Stats.Kills);
                    var deaths = matches.Sum(m => m.Participants[0].Stats.Deaths);
                    var assists = matches.Sum(m => m.Participants[0].Stats.Assists);
                    KDA = $"{killed}/{deaths}/{assists}";
                    matches.ForEach(async m =>
                    {
                        m.Participants[0].ChampionIcon = await _gameResourceManager.GetChampoinIconByIdAsync(m.Participants[0].ChampionId);
                    });
                    RecentMatches = CollectionViewSource.GetDefaultView(matches) as ListCollectionView;
                    IsLoading = false;
                }
            }
        }

        public override void OnNavigatedFrom(NavigationContext navigationContext)
        {
            RecentMatches = null;
            SelectedMatchTypeIndex = 0;
        }

        public Rank[] Ranks { get; set; }

        private int _wins;
        public int Wins
        {
            get { return _wins; }
            set
            {
                SetProperty(ref _wins, value);
                Losses = 20 - value;
            }
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

        private ChampionMastery _mastry1;
        public ChampionMastery Mastery1
        {
            get { return _mastry1; }
            set { SetProperty(ref _mastry1, value); }
        }

        private ChampionMastery _mastery2;
        public ChampionMastery Mastery2
        {
            get { return _mastery2; }
            set { SetProperty(ref _mastery2, value); }
        }

        private ChampionMastery _mastery3;
        public ChampionMastery Mastery3
        {
            get { return _mastery3; }
            set { SetProperty(ref _mastery3, value); }
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
                {ParameterNames.Matches,_recentMatches.OfType<Match>().ToList()},
            };
            RegionManager.RequestNavigate(CanModify ? RegionNames.SummonerContent : RegionNames.SearchContent, RegionNames.MatchHistoryView, parameters);
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
                {ParameterNames.Matches,_recentMatches.OfType<Match>().ToList()},
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
        public DelegateCommand RefershCommand =>
            _refreshCommand ?? (_refreshCommand = new DelegateCommand(ExecuteRefershCommand));
        void ExecuteRefershCommand()
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

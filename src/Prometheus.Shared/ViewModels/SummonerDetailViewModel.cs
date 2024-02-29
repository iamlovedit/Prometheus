using Newtonsoft.Json;
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
using System.Collections.Generic;
using System.Linq;
using System.Windows;

namespace Prometheus.Shared.ViewModels
{
    public class SummonerDetailViewModel : RegionViewModelBase
    {
        private readonly ISummonerService _summonerService;
        private readonly IGameResourceManager _gameResourceManager;
        private readonly IEventAggregator _eventAggregator;
        private readonly IDialogService _dialogService;
        private readonly IResourceService _resourceService;
        private readonly static Dictionary<Tier, string> _tierIconReosourceMap = new()
        {
            { Tier.UNRANKED,"Career.Rank.Tier.Unranked"},
            { Tier.IRON,"Career.Rank.Tier.Iron"},
            { Tier.BRONZE,"Career.Rank.Tier.Bronze"},
            { Tier.GOLD,"Career.Rank.Tier.Gold"},
            { Tier.PLATINUM,"Career.Rank.Tier.Platinum"},
            { Tier.EMERALD,"Career.Rank.Tier.Emerald"},
            { Tier.DIAMOND,"Career.Rank.Tier.Diamond"},
            { Tier.MASTER,"Career.Rank.Tier.Master"},
            { Tier.GRANDMASTER,"Career.Rank.Tier.Grandmaster"},
            { Tier.CHALLENGER,"Career.Rank.Tier.Challenger"},
        };
        public SummonerDetailViewModel(IRegionManager regionManager, IEventAggregator eventAggregator, IContainerExtension containerExtension) : base(regionManager)
        {
            _eventAggregator = eventAggregator;
            _eventAggregator.GetEvent<ConnectLCUEvent>().Subscribe(OnConnectHandler);
            _resourceService = containerExtension.Resolve<IResourceService>();
            _eventAggregator.GetEvent<LanguageSwitchedEvent>().Subscribe(() =>
            {
                FlexTier = _resourceService.FindResource<string>(_tierIconReosourceMap[_flex.Tier]);
                SoloTier = _resourceService.FindResource<string>(_tierIconReosourceMap[_solo.Tier]);
            });
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
            if (navigationContext.Parameters.TryGetValue<SummonerAccount>(ParameterNames.Summoner, out var summoner))
            {
                Summoner = summoner;
                IsPublic = summoner.Privacy == "PUBLIC";
                var jsonValue = await _gameResourceManager.GetBackgroundSkinId();
                if (!string.IsNullOrEmpty(jsonValue))
                {
                    var skinId = JObject.Parse(jsonValue)["backgroundSkinId"].ToObject<int>();
                    BackgroundSkin = await _gameResourceManager.GetBackgroundSkinByIdAsync(skinId);
                }
                ProfileIcon = await _gameResourceManager.GetProfileIconByIdAsync(Summoner.ProfileIconId);

                var rankJson = await _summonerService.GetRankStatsByPuuid(Summoner.Puuid);
                if (!string.IsNullOrEmpty(rankJson))
                {
                    var jObject = JObject.Parse(rankJson);
                    Flex = jObject["queueMap"]["RANKED_FLEX_SR"].ToObject<Rank>();
                    Solo = jObject["queueMap"]["RANKED_SOLO_5x5"].ToObject<Rank>();
                    SoloIcon = _resourceService.GetTierIconResourceUri(Solo.Tier.ToString().ToLower());
                    FlexIcon = _resourceService.GetTierIconResourceUri(Flex.Tier.ToString().ToLower());
                    FlexTier = _resourceService.FindResource<string>(_tierIconReosourceMap[_flex.Tier]);
                    SoloTier = _resourceService.FindResource<string>(_tierIconReosourceMap[_solo.Tier]);
                }
                var mathchesJosn = await _summonerService.GetRecentMatchesByPuuid(Summoner.Puuid);
                if (!string.IsNullOrEmpty(mathchesJosn))
                {
                    var jObject = JObject.Parse(mathchesJosn);
                    RecentMatches = jObject["games"]["games"].ToObject<List<Match>>();
                    RecentMatches.ForEach(async m =>
                    {
                        m.Participants[0].ChampionIcon = await _gameResourceManager.GetChampoinIconByIdAsync(m.Participants[0].ChampionId);
                        m.Participants[0].Spell1Icon = await _gameResourceManager.GetSpellIconByIdAsync(m.Participants[0].Spell1Id);
                        m.Participants[0].Spell2Icon = await _gameResourceManager.GetSpellIconByIdAsync(m.Participants[0].Spell2Id);
                        m.Participants[0].Stats.PerkIcon = await _gameResourceManager.GetPerkIconByIdAsync(m.Participants[0].Stats.Perk0);
                        m.Participants[0].Stats.Item0Icon = await _gameResourceManager.GetEquipmentIconByIdAsync(m.Participants[0].Stats.Item0);
                        m.Participants[0].Stats.Item1Icon = await _gameResourceManager.GetEquipmentIconByIdAsync(m.Participants[0].Stats.Item1);
                        m.Participants[0].Stats.Item2Icon = await _gameResourceManager.GetEquipmentIconByIdAsync(m.Participants[0].Stats.Item2);
                        m.Participants[0].Stats.Item3Icon = await _gameResourceManager.GetEquipmentIconByIdAsync(m.Participants[0].Stats.Item3);
                        m.Participants[0].Stats.Item4Icon = await _gameResourceManager.GetEquipmentIconByIdAsync(m.Participants[0].Stats.Item4);
                        m.Participants[0].Stats.Item5Icon = await _gameResourceManager.GetEquipmentIconByIdAsync(m.Participants[0].Stats.Item5);
                        m.Participants[0].Stats.Item6Icon = await _gameResourceManager.GetEquipmentIconByIdAsync(m.Participants[0].Stats.Item6);
                    });
                }
            }

            if (navigationContext.Parameters.TryGetValue<bool>(ParameterNames.CanEdit, out var canEdit))
            {
                CanModify = canEdit;
            }
        }

        private byte _wins;
        public byte Wins
        {
            get { return _wins; }
            set
            {
                SetProperty(ref _wins, value);
                Losses = (byte)(20 - value);
            }
        }

        private byte _losses;
        public byte Losses
        {
            get { return _losses; }
            set { SetProperty(ref _losses, value); }
        }

        private List<Match> _recentMatches;
        public List<Match> RecentMatches
        {
            get { return _recentMatches; }
            set
            {
                SetProperty(ref _recentMatches, value);
                Wins = (byte)value.Where(m => m.Participants[0].Stats.Win).Count();
                var killed = value.Sum(m => m.Participants[0].Stats.Kills);
                var deaths = value.Sum(m => m.Participants[0].Stats.Deaths);
                var assists = value.Sum(m => m.Participants[0].Stats.Assists);
                KDA = $"{killed}/{deaths}/{assists}";
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

        private string _soloTier;
        public string SoloTier
        {
            get { return _soloTier; }
            set { SetProperty(ref _soloTier, value); }
        }

        private string _flexTier;
        public string FlexTier
        {
            get { return _flexTier; }
            set { SetProperty(ref _flexTier, value); }
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

        private SummonerAccount _selectedSummoner;
        public SummonerAccount SelectedSummoner
        {
            get { return _selectedSummoner; }
            set { SetProperty(ref _selectedSummoner, value); }
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

        private DelegateCommand _copyCommand;
        public DelegateCommand CopyCommand =>
            _copyCommand ?? (_copyCommand = new DelegateCommand(ExecuteCopyCommand));
        void ExecuteCopyCommand()
        {
            Clipboard.SetText(_summoner.DisplayName + '#' + _summoner.TagLine);
        }
    }
}

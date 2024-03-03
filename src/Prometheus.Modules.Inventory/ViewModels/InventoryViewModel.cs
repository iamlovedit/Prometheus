using Microsoft.Win32;
using Prism.Commands;
using Prism.Ioc;
using Prism.Regions;
using Prometheus.Core;
using Prometheus.Core.Models;
using Prometheus.Core.Mvvm;
using Prometheus.Services.Interfaces.Client;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Windows.Data;
namespace Prometheus.Modules.Inventory.ViewModels
{
    public class InventoryViewModel : RegionViewModelBase
    {
        private readonly IGameResourceManager _gameResourceManager;
        private readonly Dictionary<int, List<SkinBasic>> _skinsCache;
        private List<ChampionSummary> _championsSummary;
        public InventoryViewModel(IRegionManager regionManager, IContainerExtension containerExtension,
            IGameResourceManager gameResourceManager) : base(regionManager)
        {
            _gameResourceManager = gameResourceManager;
            _skinsCache = containerExtension.Resolve<Dictionary<int, List<SkinBasic>>>(ParameterNames.SkinsCache);
        }

        private string _keyword;
        public string Keyword
        {
            get { return _keyword; }
            set
            {
                SetProperty(ref _keyword, value);
                if (string.IsNullOrEmpty(Keyword))
                {
                    _champions.Filter = null;
                }
            }
        }

        private List<SkinBasic> _skins;
        public List<SkinBasic> Skins
        {
            get { return _skins; }
            set
            {
                SetProperty(ref _skins, value);
            }
        }

        private ObservableCollection<ProfileIcon> _profileIcons;
        public ObservableCollection<ProfileIcon> ProfileIcons
        {
            get { return _profileIcons; }
            set { SetProperty(ref _profileIcons, value); }
        }



        private ICollectionView _champions;
        public ICollectionView Champions
        {
            get { return _champions; }
            set { SetProperty(ref _champions, value); }
        }

        private ChampionSummary _selectedChampion;
        public ChampionSummary SelectedChampion
        {
            get { return _selectedChampion; }
            set
            {
                SetProperty(ref _selectedChampion, value);
            }
        }

        public async override void OnNavigatedTo(NavigationContext navigationContext)
        {
            if (_championsSummary is null)
            {
                var allChampions = await _gameResourceManager.GetChampionSummarysAsync();
                _championsSummary = [];
                foreach (var champion in allChampions)
                {
                    if (champion.Id == -1)
                    {
                        continue;
                    }
                    champion.IconUri = await _gameResourceManager.GetChampoinIconByIdAsync(champion.Id);
                    _championsSummary.Add(champion);
                }
            }
            Champions = CollectionViewSource.GetDefaultView(_championsSummary);
            SelectedChampion = _championsSummary.FirstOrDefault();

            //if (_profileIcons is null)
            //{
            //    _profileIcons = [];
            //    var icons = await _gameResourceManager.GetProfileIconsAsync();
            //    foreach (var icon in icons)
            //    {
            //        if (icon.Id == 0)
            //        {
            //            continue;
            //        }
            //        icon.IconPath = await _gameResourceManager.GetProfileIconByIdAsync(icon.Id);
            //        if (string.IsNullOrEmpty(icon.IconPath))
            //        {
            //            continue;
            //        }
            //        ProfileIcons.Add(icon);
            //        RaisePropertyChanged(nameof(ProfileIcons));
            //    }
            //    IsLoading = false;
            //}
        }

        private bool _isLoading = true;
        public bool IsLoading
        {
            get { return _isLoading; }
            set { SetProperty(ref _isLoading, value); }
        }


        private DelegateCommand _searchCommand;
        public DelegateCommand SearchCommand =>
            _searchCommand ?? (_searchCommand = new DelegateCommand(ExecuteSearchCommand));
        void ExecuteSearchCommand()
        {
            _champions.Filter = (o) =>
            {
                if (o is ChampionSummary champion)
                {
                    return champion.Name.Contains(_keyword) || champion.Alias.Contains(_keyword);
                }
                return false;
            };
            Skins = null;
        }

        private DelegateCommand _selctionChangeCommand;
        public DelegateCommand SelectionChangedCommand =>
            _selctionChangeCommand ?? (_selctionChangeCommand = new DelegateCommand(ExecuteSelectionChangedCommand));
        async void ExecuteSelectionChangedCommand()
        {
            if (_selectedChampion is null)
            {
                return;
            }
            var id = _selectedChampion.Id * 1000;
            if (_skinsCache.TryGetValue(id, out var skins))
            {
                Skins = skins;
            }
            else
            {
                Skins = await _gameResourceManager.GetSkinsByChampionIdAsync(id);
                _skinsCache.Add(id, _skins);
            }
        }

        private DelegateCommand<SkinBasic> _downloadCommand;
        public DelegateCommand<SkinBasic> DownloadCommand =>
            _downloadCommand ?? (_downloadCommand = new DelegateCommand<SkinBasic>(ExecuteDownloadCommand));
        void ExecuteDownloadCommand(SkinBasic skin)
        {
            var dialog = new SaveFileDialog()
            {
                FileName = $"{skin.Name}{Path.GetExtension(skin.Uri)}",
            };
            if (dialog?.ShowDialog() ?? false)
            {
                File.Copy(skin.Uri, dialog.FileName, true);
            }
        }
    }
}

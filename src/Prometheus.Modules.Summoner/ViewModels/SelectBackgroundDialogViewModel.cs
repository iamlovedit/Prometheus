using Prism.Commands;
using Prism.Ioc;
using Prism.Mvvm;
using Prism.Services.Dialogs;
using Prometheus.Core;
using Prometheus.Core.Models;
using Prometheus.Core.Tasks;
using Prometheus.Services.Interfaces.Client;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Data;

namespace Prometheus.Modules.Summoner.ViewModels
{
    public class SelectBackgroundDialogViewModel : BindableBase, IDialogAware
    {
        private readonly Dictionary<int, List<SkinBasic>> _skinsCache;
        private readonly IGameResourceManager _gameResourceManager;
        public SelectBackgroundDialogViewModel(IGameResourceManager gameResourceManager, IContainerExtension containerExtension)
        {
            _gameResourceManager = gameResourceManager;
            _skinsCache = containerExtension.Resolve<Dictionary<int, List<SkinBasic>>>(ParameterNames.SkinsCache);
        }

        public string Title { get; }

        public event Action<IDialogResult> RequestClose;

        public bool CanCloseDialog()
        {
            return true;
        }

        public void OnDialogClosed()
        {

        }

        public void OnDialogOpened(IDialogParameters parameters)
        {
            OnDialogOpenedAsync().Observe("Loading champion backgrounds");
        }

        private async Task OnDialogOpenedAsync()
        {
            var allChampions = await _gameResourceManager.GetChampionSummarysAsync();
            if (allChampions is null)
            {
                return;
            }

            var champions = new List<ChampionSummary>();
            foreach (var champion in allChampions)
            {
                if (champion.Id == -1)
                {
                    continue;
                }
                champion.IconUri = await _gameResourceManager.GetChampoinIconByIdAsync(champion.Id);
                champions.Add(champion);
            }
            Champions = CollectionViewSource.GetDefaultView(champions);
            SelectedChampion = champions.FirstOrDefault();
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

        private List<SkinBasic> _skins;
        public List<SkinBasic> Skins
        {
            get { return _skins; }
            set
            {
                SetProperty(ref _skins, value);
                if (value != null)
                {
                    SelectedSkin = value.FirstOrDefault();
                }
            }
        }

        private SkinBasic _selectedSkin;
        public SkinBasic SelectedSkin
        {
            get { return _selectedSkin; }
            set { SetProperty(ref _selectedSkin, value); }
        }

        private bool _isSync;
        public bool IsSync
        {
            get { return _isSync; }
            set { SetProperty(ref _isSync, value); }
        }


        private DelegateCommand _selctionChangeCommand;
        public DelegateCommand SelectionChangedCommand =>
            _selctionChangeCommand ?? (_selctionChangeCommand = new DelegateCommand(ExecuteSelectionChangedCommand));
        void ExecuteSelectionChangedCommand()
        {
            ExecuteSelectionChangedCommandAsync().Observe("Loading champion background skins");
        }

        private async Task ExecuteSelectionChangedCommandAsync()
        {
            if (_selectedChampion is null)
            {
                return;
            }
            var id = _selectedChampion.Id;
            if (_skinsCache.TryGetValue(id, out var skins))
            {
                Skins = skins;
            }
            else
            {
                Skins = await _gameResourceManager.GetSkinsByChampionIdAsync(id);
                _skinsCache[id] = _skins;
            }
        }


        private DelegateCommand _comfirmCommand;
        public DelegateCommand ComfirmCommand =>
            _comfirmCommand ?? (_comfirmCommand = new DelegateCommand(ExecuteComfirmCommand));
        void ExecuteComfirmCommand()
        {
            ExecuteComfirmCommandAsync().Observe("Applying the selected champion background");
        }

        private async Task ExecuteComfirmCommandAsync()
        {
            if (_selectedSkin is null)
            {
                return;
            }

            if (_isSync)
            {
                await _gameResourceManager.SetBackgroundSkinId(_selectedSkin.Id);
            }
            var parameters = new DialogParameters()
            {
                {ParameterNames.SelectedSkinUri,_selectedSkin.Uri }
            };
            RequestClose?.Invoke(new DialogResult(ButtonResult.OK, parameters));
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

        private DelegateCommand<string> _searchCommand;
        public DelegateCommand<string> SearchCommand =>
            _searchCommand ?? (_searchCommand = new DelegateCommand<string>(ExecuteSearchCommand));
        void ExecuteSearchCommand(string keyword)
        {
            _champions.Filter = (o) =>
            {
                if (o is ChampionSummary champion)
                {
                    return champion.Name.Contains(keyword) || champion.Alias.Contains(keyword);
                }
                return false;
            };
            Skins = null;
            SelectedChampion = null;
        }
    }
}

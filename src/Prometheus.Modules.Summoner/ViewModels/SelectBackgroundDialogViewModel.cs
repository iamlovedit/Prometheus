using Prism.Commands;
using Prism.Ioc;
using Prism.Mvvm;
using Prism.Services.Dialogs;
using Prometheus.Core;
using Prometheus.Core.Models;
using Prometheus.Core.Tasks;
using Prometheus.Services.Interfaces.Client;
using System.ComponentModel;

namespace Prometheus.Modules.Summoner.ViewModels
{
    public class SelectBackgroundDialogViewModel : BindableBase, IDialogAware
    {
        private const int MaximumConcurrentImageLoads = 8;

        private readonly Dictionary<int, List<SkinBasic>> _skinsCache;
        private readonly IGameResourceManager _gameResourceManager;
        private int _dialogLoadVersion;
        private int _skinLoadVersion;
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
            _dialogLoadVersion++;
            _skinLoadVersion++;
        }

        public void OnDialogOpened(IDialogParameters parameters)
        {
            OnDialogOpenedAsync().Observe("Loading champion backgrounds");
        }

        private async Task OnDialogOpenedAsync()
        {
            var version = ++_dialogLoadVersion;
            var allChampions = await _gameResourceManager.GetChampionSummarysAsync();
            if (allChampions is null || version != _dialogLoadVersion)
            {
                return;
            }

            var champions = allChampions
                .Where(champion => champion.Id != -1)
                .ToList();
            Champions = CollectionViewSource.GetDefaultView(champions);
            SelectedChampion = champions.FirstOrDefault();

            var paths = await LoadBoundedAsync(
                champions,
                champion => _gameResourceManager.GetChampoinIconByIdAsync(champion.Id));
            if (version != _dialogLoadVersion)
            {
                return;
            }

            for (var index = 0; index < champions.Count; index++)
            {
                champions[index].IconUri = paths[index];
            }
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
            var version = ++_skinLoadVersion;
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
                var loadedSkins = await _gameResourceManager.GetSkinsByChampionIdAsync(id);
                if (version != _skinLoadVersion || _selectedChampion?.Id != id)
                {
                    return;
                }

                Skins = loadedSkins;
                if (loadedSkins is { Count: > 0 })
                {
                    _skinsCache[id] = loadedSkins;
                }
            }
        }

        private static async Task<TResult[]> LoadBoundedAsync<TSource, TResult>(
            IReadOnlyList<TSource> values,
            Func<TSource, Task<TResult>> load)
        {
            using var gate = new SemaphoreSlim(
                MaximumConcurrentImageLoads,
                MaximumConcurrentImageLoads);
            return await Task.WhenAll(values.Select(async value =>
            {
                await gate.WaitAsync().ConfigureAwait(false);
                try
                {
                    return await load(value).ConfigureAwait(false);
                }
                finally
                {
                    gate.Release();
                }
            }));
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

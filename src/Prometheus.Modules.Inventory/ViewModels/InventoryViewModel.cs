using HandyControl.Controls;
using HandyControl.Data;
using Microsoft.Win32;
using Prism.Commands;
using Prism.Ioc;
using Prism.Regions;
using Prometheus.Core;
using Prometheus.Core.Models;
using Prometheus.Core.Mvvm;
using Prometheus.Core.Tasks;
using Prometheus.Services.Interfaces.Client;
using System.Collections.ObjectModel;
using System.ComponentModel;
namespace Prometheus.Modules.Inventory.ViewModels
{
    public class InventoryViewModel : RegionViewModelBase
    {
        private const int MaximumConcurrentImageLoads = 8;

        private readonly IGameResourceManager _gameResourceManager;
        private readonly IResourceService _resourceService;
        private readonly Dictionary<int, List<SkinBasic>> _skinsCache;
        private List<ChampionSummary> _championsSummary;
        private List<ProfileIcon> _allIcons;
        private int _profileIconsLoadVersion;

        public InventoryViewModel(IRegionManager regionManager, IContainerExtension containerExtension,
            IGameResourceManager gameResourceManager, IResourceService resourceService) : base(regionManager)
        {
            _gameResourceManager = gameResourceManager;
            _resourceService = resourceService;
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
                if (SetProperty(ref _skins, value))
                {
                    _downloadAllCommand?.RaiseCanExecuteChanged();
                }
            }
        }

        private ObservableCollection<ProfileIcon> _profileIcons = [];
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

        public override void OnNavigatedTo(NavigationContext navigationContext)
        {
            OnNavigatedToAsync().Observe("Loading inventory data");
        }

        private async Task OnNavigatedToAsync()
        {
            var championMetadataTask = _championsSummary is null
                ? _gameResourceManager.GetChampionSummarysAsync()
                : Task.FromResult<List<ChampionSummary>>(null);
            var profileMetadataTask = _allIcons is null
                ? _gameResourceManager.GetProfileIconsAsync()
                : Task.FromResult<List<ProfileIcon>>(null);

            await Task.WhenAll(championMetadataTask, profileMetadataTask);

            if (_championsSummary is null)
            {
                var allChampions = await championMetadataTask;
                if (allChampions != null)
                {
                    _championsSummary = allChampions
                        .Where(champion => champion.Id != -1)
                        .ToList();
                    Champions = CollectionViewSource.GetDefaultView(_championsSummary);
                    SelectedChampion = _championsSummary.FirstOrDefault();
                    LoadChampionIconsAsync(_championsSummary)
                        .Observe("Loading inventory champion icons");
                }
            }
            if (_allIcons is null)
            {
                _allIcons = await profileMetadataTask;
                if (_allIcons != null)
                {
                    CalculatePageCount(_selectdCount);
                    await ReloadProfileIconsAsync(1);
                }
            }
        }

        private bool _isLoading = true;
        public bool IsLoading
        {
            get { return _isLoading; }
            set { SetProperty(ref _isLoading, value); }
        }

        public int[] PageCounts { get; } = [50, 100];

        private int _selectdCount = 50;
        public int SelectdCount
        {
            get { return _selectdCount; }
            set
            {
                if (!SetProperty(ref _selectdCount, value))
                {
                    return;
                }

                CalculatePageCount(value);
                if (_allIcons is not null)
                {
                    ReloadProfileIconsAsync(1).Observe("Reloading inventory profile icons after page size change");
                }
            }
        }
        private int _pageCount;
        public int PageCount
        {
            get { return _pageCount; }
            set { SetProperty(ref _pageCount, value); }
        }

        private int _pageIndex = 1;
        public int PageIndex
        {
            get { return _pageIndex; }
            set { SetProperty(ref _pageIndex, value); }
        }

        private DelegateCommand<FunctionEventArgs<int>> _pageChangedCommand;
        public DelegateCommand<FunctionEventArgs<int>> PageChangedCommand =>
            _pageChangedCommand ?? (_pageChangedCommand = new DelegateCommand<FunctionEventArgs<int>>(ExecutePageChangedCommand));
        void ExecutePageChangedCommand(FunctionEventArgs<int> parameter)
        {
            ExecutePageChangedCommandAsync(parameter).Observe("Loading inventory profile icons");
        }

        private async Task ExecutePageChangedCommandAsync(FunctionEventArgs<int> parameter)
        {
            await ReloadProfileIconsAsync(parameter.Info);
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
        void ExecuteSelectionChangedCommand()
        {
            ExecuteSelectionChangedCommandAsync().Observe("Loading champion skins");
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
                var loadedSkins = await _gameResourceManager.GetSkinsByChampionIdAsync(id);
                Skins = loadedSkins;
                if (loadedSkins is { Count: > 0 })
                {
                    _skinsCache[id] = loadedSkins;
                }
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

        private bool _isDownloadingAll;
        public bool IsDownloadingAll
        {
            get { return _isDownloadingAll; }
            set
            {
                if (SetProperty(ref _isDownloadingAll, value))
                {
                    _downloadAllCommand?.RaiseCanExecuteChanged();
                }
            }
        }

        private DelegateCommand _downloadAllCommand;
        public DelegateCommand DownloadAllCommand =>
            _downloadAllCommand ??= new DelegateCommand(
                ExecuteDownloadAllCommand,
                CanExecuteDownloadAllCommand);

        private bool CanExecuteDownloadAllCommand()
        {
            return !IsDownloadingAll && _skins is { Count: > 0 };
        }

        private void ExecuteDownloadAllCommand()
        {
            ExecuteDownloadAllCommandAsync().Observe("Downloading all champion skins");
        }

        private async Task ExecuteDownloadAllCommandAsync()
        {
            var dialog = new OpenFolderDialog
            {
                Title = _resourceService.FindResource<string>("Inventory.Skins.DownloadAll.SelectFolder"),
                Multiselect = false
            };
            if (dialog.ShowDialog() != true)
            {
                return;
            }

            var skins = _skins.ToArray();
            IsDownloadingAll = true;
            try
            {
                var result = await Task.Run(() => SaveSkins(skins, dialog.FolderName));
                if (result.FailedCount == 0)
                {
                    Growl.Info(string.Format(
                        _resourceService.FindResource<string>("Inventory.Skins.DownloadAll.Success"),
                        result.SavedCount));
                }
                else
                {
                    Growl.Error(string.Format(
                        _resourceService.FindResource<string>("Inventory.Skins.DownloadAll.Partial"),
                        result.SavedCount,
                        result.FailedCount));
                }
            }
            catch (Exception exception)
            {
                Growl.Error(string.Format(
                    _resourceService.FindResource<string>("Inventory.Skins.DownloadAll.Error"),
                    exception.Message));
            }
            finally
            {
                IsDownloadingAll = false;
            }
        }

        private static (int SavedCount, int FailedCount) SaveSkins(
            IEnumerable<SkinBasic> skins,
            string folderPath)
        {
            var savedCount = 0;
            var failedCount = 0;
            var usedFileNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var skin in skins)
            {
                try
                {
                    if (skin is null || string.IsNullOrWhiteSpace(skin.Uri) || !File.Exists(skin.Uri))
                    {
                        failedCount++;
                        continue;
                    }

                    var extension = Path.GetExtension(skin.Uri);
                    var baseName = GetSafeFileName(skin.Name, skin.Id);
                    var fileName = GetUniqueFileName(baseName, extension, skin.Id, usedFileNames);
                    var targetPath = Path.Combine(folderPath, fileName);

                    if (!string.Equals(
                        Path.GetFullPath(skin.Uri),
                        Path.GetFullPath(targetPath),
                        StringComparison.OrdinalIgnoreCase))
                    {
                        File.Copy(skin.Uri, targetPath, true);
                    }

                    savedCount++;
                }
                catch
                {
                    failedCount++;
                }
            }

            return (savedCount, failedCount);
        }

        private static string GetSafeFileName(string name, int skinId)
        {
            var invalidCharacters = Path.GetInvalidFileNameChars();
            var safeName = new string((name ?? string.Empty)
                .Select(character => invalidCharacters.Contains(character) ? '_' : character)
                .ToArray())
                .Trim()
                .TrimEnd('.');

            return string.IsNullOrWhiteSpace(safeName)
                ? $"skin-{skinId}"
                : safeName;
        }

        private static string GetUniqueFileName(
            string baseName,
            string extension,
            int skinId,
            ISet<string> usedFileNames)
        {
            var fileName = $"{baseName}{extension}";
            if (usedFileNames.Add(fileName))
            {
                return fileName;
            }

            fileName = $"{baseName}-{skinId}{extension}";
            var suffix = 2;
            while (!usedFileNames.Add(fileName))
            {
                fileName = $"{baseName}-{skinId}-{suffix}{extension}";
                suffix++;
            }

            return fileName;
        }

        private DelegateCommand<ProfileIcon> _downloadIconCommand;
        public DelegateCommand<ProfileIcon> DownloadIconCommand =>
            _downloadIconCommand ?? (_downloadIconCommand = new DelegateCommand<ProfileIcon>(ExecuteDownloadIconCommand));
        void ExecuteDownloadIconCommand(ProfileIcon icon)
        {
            var dialog = new SaveFileDialog()
            {
                FileName = $"{icon.Id}{Path.GetExtension(icon.IconPath)}",
            };
            if (dialog?.ShowDialog() ?? false)
            {
                File.Copy(icon.IconPath, dialog.FileName, true);
            }
        }


        private void CalculatePageCount(int pageSize)
        {
            if (_allIcons is null || pageSize <= 0)
            {
                PageCount = 0;
                return;
            }

            if (_allIcons.Count % pageSize == 0)
            {
                PageCount = _allIcons.Count / pageSize;
            }
            else
            {
                PageCount = (int)Math.Floor((double)(_allIcons.Count / pageSize)) + 1;
            }
        }

        private async Task ReloadProfileIconsAsync(int pageIndex)
        {
            if (_allIcons is null)
            {
                return;
            }

            var loadVersion = ++_profileIconsLoadVersion;
            var targetPageIndex = PageCount > 0
                ? Math.Clamp(pageIndex, 1, PageCount)
                : 1;

            IsLoading = true;
            try
            {
                var icons = await LoadProfileIconsAsync(targetPageIndex, _selectdCount);
                if (loadVersion != _profileIconsLoadVersion)
                {
                    return;
                }

                PageIndex = targetPageIndex;
                ProfileIcons = new ObservableCollection<ProfileIcon>(icons);
            }
            finally
            {
                if (loadVersion == _profileIconsLoadVersion)
                {
                    IsLoading = false;
                }
            }
        }

        private async Task<List<ProfileIcon>> LoadProfileIconsAsync(int pageIndex, int pageSize)
        {
            var icons = _allIcons
                .Skip((pageIndex - 1) * pageSize)
                .Take(pageSize)
                .Where(icon => icon.Id != 0)
                .ToArray();
            var paths = await LoadBoundedAsync(
                icons,
                icon => _gameResourceManager.GetProfileIconByIdAsync(icon.Id));
            for (var index = 0; index < icons.Length; index++)
            {
                icons[index].IconPath = paths[index];
            }

            return icons.Where(icon => !string.IsNullOrEmpty(icon.IconPath)).ToList();
        }

        private async Task LoadChampionIconsAsync(
            IReadOnlyList<ChampionSummary> champions)
        {
            var paths = await LoadBoundedAsync(
                champions,
                champion => _gameResourceManager.GetChampoinIconByIdAsync(champion.Id));
            for (var index = 0; index < champions.Count; index++)
            {
                champions[index].IconUri = paths[index];
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
    }
}

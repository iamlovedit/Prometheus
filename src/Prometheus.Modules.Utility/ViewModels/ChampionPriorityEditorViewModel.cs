using Prism.Commands;
using Prism.Mvvm;
using Prometheus.Core.Models;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Data;

namespace Prometheus.Modules.Utility.ViewModels
{
    /// <summary>
    /// Reusable searchable priority editor for champion automation preferences.
    /// Each instance owns an independent collection view so pick and ban searches
    /// never overwrite one another's filters.
    /// </summary>
    public sealed class ChampionPriorityEditorViewModel : BindableBase
    {
        private readonly Action _ensureChampionCatalogLoaded;
        private readonly Action<IReadOnlyList<int>> _persistChampionIds;
        private IReadOnlyList<ChampionSummary> _champions = [];
        private ICollectionView _championOptions;
        private bool _isSynchronizingSelector;
        private string _searchText = string.Empty;
        private bool _isDropDownOpen;
        private ChampionSummary _selectedChampion;
        private ChampionSummary _selectedPreferredChampion;

        public ChampionPriorityEditorViewModel(
            Action ensureChampionCatalogLoaded,
            Action<IReadOnlyList<int>> persistChampionIds)
        {
            _ensureChampionCatalogLoaded = ensureChampionCatalogLoaded ??
                throw new ArgumentNullException(nameof(ensureChampionCatalogLoaded));
            _persistChampionIds = persistChampionIds ??
                throw new ArgumentNullException(nameof(persistChampionIds));
        }

        public ICollectionView ChampionOptions
        {
            get => _championOptions;
            private set => SetProperty(ref _championOptions, value);
        }

        public string SearchText
        {
            get => _searchText;
            set
            {
                if (_isSynchronizingSelector)
                {
                    return;
                }

                var next = value ?? string.Empty;
                if (string.Equals(_searchText, next, StringComparison.Ordinal))
                {
                    return;
                }

                _isSynchronizingSelector = true;
                try
                {
                    SetProperty(ref _searchText, next, nameof(SearchText));
                    var matchesSelection = string.Equals(
                        next,
                        SelectedChampion?.Name,
                        StringComparison.CurrentCultureIgnoreCase);
                    if (!matchesSelection && SelectedChampion is not null)
                    {
                        SetProperty(
                            ref _selectedChampion,
                            null,
                            nameof(SelectedChampion));
                        AddChampionCommand.RaiseCanExecuteChanged();
                        RaisePropertyChanged(nameof(SearchText));
                    }

                    if (!matchesSelection)
                    {
                        ChampionOptions?.Refresh();
                        if (!string.IsNullOrWhiteSpace(next))
                        {
                            IsDropDownOpen = true;
                        }
                    }
                }
                finally
                {
                    _isSynchronizingSelector = false;
                }
            }
        }

        public bool IsDropDownOpen
        {
            get => _isDropDownOpen;
            set => SetProperty(ref _isDropDownOpen, value);
        }

        public ChampionSummary SelectedChampion
        {
            get => _selectedChampion;
            set
            {
                if (!SetProperty(ref _selectedChampion, value))
                {
                    return;
                }

                if (!_isSynchronizingSelector && value is not null)
                {
                    _isSynchronizingSelector = true;
                    try
                    {
                        SetProperty(
                            ref _searchText,
                            value.Name ?? string.Empty,
                            nameof(SearchText));
                        IsDropDownOpen = false;
                    }
                    finally
                    {
                        _isSynchronizingSelector = false;
                    }
                }

                AddChampionCommand.RaiseCanExecuteChanged();
            }
        }

        public ObservableCollection<ChampionSummary> PreferredChampions { get; } = [];

        public ChampionSummary SelectedPreferredChampion
        {
            get => _selectedPreferredChampion;
            set
            {
                if (SetProperty(ref _selectedPreferredChampion, value))
                {
                    RaiseCommandState();
                }
            }
        }

        public DelegateCommand OpenSelectorCommand => _openSelectorCommand ??=
            new DelegateCommand(_ensureChampionCatalogLoaded);
        private DelegateCommand _openSelectorCommand;

        public DelegateCommand AddChampionCommand => _addChampionCommand ??=
            new DelegateCommand(AddChampion, CanAddChampion);
        private DelegateCommand _addChampionCommand;

        public DelegateCommand RemoveChampionCommand => _removeChampionCommand ??=
            new DelegateCommand(
                RemoveChampion,
                () => SelectedPreferredChampion is not null);
        private DelegateCommand _removeChampionCommand;

        public DelegateCommand MoveChampionUpCommand => _moveChampionUpCommand ??=
            new DelegateCommand(
                () => MoveChampion(-1),
                () => GetSelectedIndex() > 0);
        private DelegateCommand _moveChampionUpCommand;

        public DelegateCommand MoveChampionDownCommand => _moveChampionDownCommand ??=
            new DelegateCommand(
                () => MoveChampion(1),
                () =>
                {
                    var index = GetSelectedIndex();
                    return index >= 0 && index < PreferredChampions.Count - 1;
                });
        private DelegateCommand _moveChampionDownCommand;

        public void ApplyPreferredChampionIds(
            IEnumerable<int> championIds,
            IReadOnlyList<ChampionSummary> champions)
        {
            _champions = champions ?? [];
            var championMap = _champions
                .Where(champion => champion is not null && champion.Id > 0)
                .ToDictionary(champion => champion.Id);

            PreferredChampions.Clear();
            foreach (var championId in championIds?
                         .Where(championId => championId > 0)
                         .Distinct() ?? [])
            {
                if (!championMap.TryGetValue(championId, out var champion))
                {
                    champion = new ChampionSummary
                    {
                        Id = championId,
                        Name = $"#{championId}",
                        Alias = string.Empty
                    };
                }

                PreferredChampions.Add(champion);
            }

            SelectedPreferredChampion = PreferredChampions.FirstOrDefault();
            RaiseCommandState();
        }

        public void SetChampionCatalog(IReadOnlyList<ChampionSummary> champions)
        {
            var preferredIds = PreferredChampions
                .Select(champion => champion.Id)
                .ToArray();
            _champions = champions ?? [];
            var optionsSource = new ObservableCollection<ChampionSummary>(_champions);
            var options = CollectionViewSource.GetDefaultView(optionsSource);
            options.Filter = FilterChampion;
            ChampionOptions = options;
            ApplyPreferredChampionIds(preferredIds, _champions);
        }

        private bool FilterChampion(object item)
        {
            if (item is not ChampionSummary champion || champion.Id <= 0)
            {
                return false;
            }

            var keyword = SearchText?.Trim();
            return string.IsNullOrWhiteSpace(keyword) ||
                   (champion.Name?.Contains(
                       keyword,
                       StringComparison.CurrentCultureIgnoreCase) ?? false) ||
                   (champion.Alias?.Contains(
                       keyword,
                       StringComparison.OrdinalIgnoreCase) ?? false);
        }

        private bool CanAddChampion()
        {
            return SelectedChampion is not null &&
                   PreferredChampions.All(
                       champion => champion.Id != SelectedChampion.Id);
        }

        private void AddChampion()
        {
            if (!CanAddChampion())
            {
                return;
            }

            PreferredChampions.Add(SelectedChampion);
            SelectedPreferredChampion = SelectedChampion;
            Persist();
            SelectedChampion = null;
            SearchText = string.Empty;
        }

        private void RemoveChampion()
        {
            var index = GetSelectedIndex();
            if (index < 0)
            {
                return;
            }

            PreferredChampions.RemoveAt(index);
            SelectedPreferredChampion = PreferredChampions.Count == 0
                ? null
                : PreferredChampions[Math.Min(index, PreferredChampions.Count - 1)];
            Persist();
        }

        private void MoveChampion(int offset)
        {
            var selectedIndex = GetSelectedIndex();
            var targetIndex = selectedIndex + offset;
            if (selectedIndex < 0 || targetIndex < 0 ||
                targetIndex >= PreferredChampions.Count)
            {
                return;
            }

            PreferredChampions.Move(selectedIndex, targetIndex);
            Persist();
            RaiseCommandState();
        }

        private int GetSelectedIndex()
        {
            return SelectedPreferredChampion is null
                ? -1
                : PreferredChampions.IndexOf(SelectedPreferredChampion);
        }

        private void Persist()
        {
            _persistChampionIds(PreferredChampions
                .Select(champion => champion.Id)
                .ToArray());
            AddChampionCommand.RaiseCanExecuteChanged();
        }

        private void RaiseCommandState()
        {
            _addChampionCommand?.RaiseCanExecuteChanged();
            _removeChampionCommand?.RaiseCanExecuteChanged();
            _moveChampionUpCommand?.RaiseCanExecuteChanged();
            _moveChampionDownCommand?.RaiseCanExecuteChanged();
        }
    }
}

using Prism.Commands;
using Prism.Events;
using Prometheus.Core.Events;
using Prometheus.Core.Models;
using Prometheus.Services.Interfaces;
using Prometheus.Services.Interfaces.Client;
using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows;
using System.Windows.Data;
using System.Windows.Threading;

namespace Prometheus.Modules.Setting.ViewModels
{
    /// <summary>
    /// Backs the "Log" settings tab. Mirrors the in-memory log buffer from
    /// <see cref="ILogHistoryService"/>, applies search/level filtering, and exposes
    /// commands for clearing and pausing the live stream.
    /// </summary>
    public class LogViewModel : TabItemViewModelBase
    {
        private readonly ILogHistoryService _logHistory;
        private readonly ObservableCollection<LogEntry> _allEntries;
        private readonly int _bufferCapacity;

        protected override string TitleResourceKey { get; set; } = "Setting.Log";

        public LogViewModel(
            IEventAggregator eventAggregator,
            IResourceService resourceService,
            ILogHistoryService logHistory)
            : base(eventAggregator, resourceService)
        {
            _logHistory = logHistory;
            _bufferCapacity = logHistory.Capacity;
            _allEntries = new ObservableCollection<LogEntry>(logHistory.GetSnapshot());
            TrimToCapacity();

            Entries = (ListCollectionView)CollectionViewSource.GetDefaultView(_allEntries);
            Entries.Filter = FilterEntry;

            logHistory.EntryLogged += HandleEntryLogged;
            logHistory.Cleared += HandleCleared;
            EventAggregator.GetEvent<LanguageSwitchedEvent>().Subscribe(RefreshCountText);

            ClearCommand = new DelegateCommand(() => logHistory.Clear());

            UpdateCounts();
        }

        /// <summary>Filtered, bindable view over the mirrored log entries.</summary>
        public ListCollectionView Entries { get; }

        public DelegateCommand ClearCommand { get; }

        private string _searchText;
        public string SearchText
        {
            get => _searchText;
            set
            {
                if (SetProperty(ref _searchText, value))
                {
                    Entries.Refresh();
                    UpdateCounts();
                }
            }
        }

        /// <summary>0 = all levels; 1..6 = Verbose..Fatal (minimum level shown).</summary>
        private int _minimumLevelIndex;
        public int MinimumLevelIndex
        {
            get => _minimumLevelIndex;
            set
            {
                if (SetProperty(ref _minimumLevelIndex, value))
                {
                    Entries.Refresh();
                    UpdateCounts();
                }
            }
        }

        private bool _autoScroll = true;
        public bool AutoScroll
        {
            get => _autoScroll;
            set => SetProperty(ref _autoScroll, value);
        }

        private bool _isPaused;
        public bool IsPaused
        {
            get => _isPaused;
            set
            {
                if (SetProperty(ref _isPaused, value) && !value)
                {
                    // Resumed: catch up to the current buffer rather than showing a stale subset.
                    ReloadSnapshot();
                }
            }
        }

        private int _totalCount;
        public int TotalCount
        {
            get => _totalCount;
            private set => SetProperty(ref _totalCount, value);
        }

        private int _filteredCount;
        public int FilteredCount
        {
            get => _filteredCount;
            private set => SetProperty(ref _filteredCount, value);
        }

        public bool HasVisibleEntries => FilteredCount > 0;

        public bool IsLogEmpty => TotalCount == 0;

        public bool HasNoFilterResults => TotalCount > 0 && FilteredCount == 0;

        public bool HasAnyEntries => TotalCount > 0;

        private string _countText;
        public string CountText
        {
            get => _countText;
            private set => SetProperty(ref _countText, value);
        }

        private bool FilterEntry(object item)
        {
            if (item is not LogEntry entry)
            {
                return false;
            }

            if (MinimumLevelIndex > 0)
            {
                var minimum = (LogLevel)(MinimumLevelIndex - 1);
                if (entry.Level < minimum)
                {
                    return false;
                }
            }

            if (!string.IsNullOrWhiteSpace(_searchText))
            {
                var comparison = StringComparison.OrdinalIgnoreCase;
                if (entry.Message?.IndexOf(_searchText, comparison) < 0
                    && entry.Exception?.IndexOf(_searchText, comparison) < 0)
                {
                    return false;
                }
            }

            return true;
        }

        private void HandleEntryLogged(object sender, LogEntryLoggedEventArgs args)
        {
            // Raised on the logging thread. Pause and AutoScroll are simple bool flags; reading
            // them cross-thread is safe (no torn reads) and we marshal all collection edits.
            if (args?.Entry is null || IsPaused)
            {
                return;
            }

            Dispatch(() =>
            {
                _allEntries.Add(args.Entry);
                TrimToCapacity();
                UpdateCounts();
            });
        }

        private void HandleCleared(object sender, EventArgs e)
        {
            Dispatch(() =>
            {
                _allEntries.Clear();
                UpdateCounts();
            });
        }

        private void ReloadSnapshot()
        {
            Dispatch(() =>
            {
                _allEntries.Clear();
                foreach (var entry in _logHistory.GetSnapshot())
                {
                    _allEntries.Add(entry);
                }

                TrimToCapacity();
                Entries.Refresh();
                UpdateCounts();
            });
        }

        private void TrimToCapacity()
        {
            while (_allEntries.Count > _bufferCapacity)
            {
                _allEntries.RemoveAt(0);
            }
        }

        private void UpdateCounts()
        {
            TotalCount = _allEntries.Count;
            FilteredCount = Entries?.Count ?? 0;
            RefreshCountText();
            RaisePropertyChanged(nameof(HasVisibleEntries));
            RaisePropertyChanged(nameof(IsLogEmpty));
            RaisePropertyChanged(nameof(HasNoFilterResults));
            RaisePropertyChanged(nameof(HasAnyEntries));
        }

        private void RefreshCountText()
        {
            var format = ResourceService.FindResource<string>("Setting.Log.Count") ?? "{0} / {1}";
            CountText = string.Format(format, FilteredCount, TotalCount);
        }

        public override void Destroy()
        {
            _logHistory.EntryLogged -= HandleEntryLogged;
            _logHistory.Cleared -= HandleCleared;
            EventAggregator.GetEvent<LanguageSwitchedEvent>().Unsubscribe(RefreshCountText);
            base.Destroy();
        }

        private static void Dispatch(Action action)
        {
            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher is null || dispatcher.CheckAccess())
            {
                action();
                return;
            }

            dispatcher.BeginInvoke(DispatcherPriority.Background, action);
        }
    }
}

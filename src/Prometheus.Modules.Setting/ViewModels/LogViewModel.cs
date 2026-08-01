using Prism.Commands;
using Prism.Events;
using Prometheus.Core.Events;
using Prometheus.Core.Models;
using Prometheus.Services.Interfaces;
using Prometheus.Services.Interfaces.Client;
using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Threading;

namespace Prometheus.Modules.Setting.ViewModels
{
    public enum LogViewMode
    {
        Operations,
        Diagnostics,
    }

    public sealed class LogFilterOption
    {
        public string Value { get; }

        public string DisplayName { get; }

        public LogFilterOption(string value, string displayName)
        {
            Value = value ?? string.Empty;
            DisplayName = displayName ?? string.Empty;
        }
    }

    /// <summary>
    /// Backs the structured log workbench. Operation results are the default view; diagnostic
    /// events and unclassified Serilog entries remain available in the troubleshooting view.
    /// </summary>
    public class LogViewModel : TabItemViewModelBase
    {
        private readonly ILogHistoryService _logHistory;
        private readonly ILoggingControlService _loggingControl;
        private readonly ObservableCollection<LogEntry> _allEntries;
        private readonly ConcurrentQueue<LogEntry> _pendingEntries = new();
        private readonly int _bufferCapacity;
        private int _drainScheduled;
        private bool _isDestroyed;
        private bool _isUpdatingCategoryOptions;
        private LogSearchQuery _searchQuery = LogSearchQuery.Empty;
        private LogViewMode _viewMode = LogViewMode.Operations;

        protected override string TitleResourceKey { get; set; } = "Setting.Log";

        public LogViewModel(
            IEventAggregator eventAggregator,
            IResourceService resourceService,
            ILogHistoryService logHistory,
            ILoggingControlService loggingControl)
            : base(eventAggregator, resourceService)
        {
            _logHistory = logHistory;
            _loggingControl = loggingControl;
            _bufferCapacity = logHistory.Capacity;
            _allEntries = new ObservableCollection<LogEntry>(loggingControl.IsEnabled
                ? logHistory.GetSnapshot()
                : []);
            TrimToCapacity();

            Entries = (ListCollectionView)CollectionViewSource.GetDefaultView(_allEntries);
            Entries.Filter = FilterEntry;

            CategoryOptions = [];
            BuildLocalizedOptions();

            logHistory.EntryLogged += HandleEntryLogged;
            logHistory.Cleared += HandleCleared;
            loggingControl.EnabledChanged += HandleLoggingEnabledChanged;
            EventAggregator.GetEvent<LanguageSwitchedEvent>().Subscribe(RefreshLocalizedState);

            ShowOperationsCommand = new DelegateCommand(() => SetViewMode(LogViewMode.Operations));
            ShowDiagnosticsCommand = new DelegateCommand(() => SetViewMode(LogViewMode.Diagnostics));
            ClearCommand = new DelegateCommand(() => logHistory.Clear());
            ResetFiltersCommand = new DelegateCommand(ResetFilters, () => HasActiveFilters);
            CloseDetailsCommand = new DelegateCommand(() => SelectedEntry = null);

            UpdateCounts();
        }

        public ListCollectionView Entries { get; }

        public ObservableCollection<LogFilterOption> CategoryOptions { get; }

        public IReadOnlyList<LogFilterOption> OriginOptions { get; private set; }

        public IReadOnlyList<LogFilterOption> OutcomeOptions { get; private set; }

        public IReadOnlyList<LogFilterOption> LevelOptions { get; private set; }

        public IReadOnlyList<LogFilterOption> TimeRangeOptions { get; private set; }

        public DelegateCommand ShowOperationsCommand { get; }

        public DelegateCommand ShowDiagnosticsCommand { get; }

        public DelegateCommand ClearCommand { get; }

        public DelegateCommand ResetFiltersCommand { get; }

        public DelegateCommand CloseDetailsCommand { get; }

        public bool IsOperationView => _viewMode == LogViewMode.Operations;

        public bool IsDiagnosticView => _viewMode == LogViewMode.Diagnostics;

        public bool IsLoggingEnabled => _loggingControl.IsEnabled;

        public bool IsLoggingDisabled => !IsLoggingEnabled;

        private bool _showIntermediateOperations;
        public bool ShowIntermediateOperations
        {
            get => _showIntermediateOperations;
            set
            {
                if (SetProperty(ref _showIntermediateOperations, value))
                {
                    RefreshView();
                }
            }
        }

        private string _searchText;
        public string SearchText
        {
            get => _searchText;
            set
            {
                if (SetProperty(ref _searchText, value))
                {
                    _searchQuery = LogSearchQuery.Parse(value);
                    RefreshView();
                }
            }
        }

        private string _selectedCategory = string.Empty;
        public string SelectedCategory
        {
            get => _selectedCategory;
            set
            {
                if (_isUpdatingCategoryOptions)
                {
                    return;
                }

                if (SetProperty(ref _selectedCategory, value ?? string.Empty))
                {
                    RefreshView();
                }
            }
        }

        private string _selectedOrigin = string.Empty;
        public string SelectedOrigin
        {
            get => _selectedOrigin;
            set
            {
                if (SetProperty(ref _selectedOrigin, value ?? string.Empty))
                {
                    RefreshView();
                }
            }
        }

        private string _selectedOutcome = string.Empty;
        public string SelectedOutcome
        {
            get => _selectedOutcome;
            set
            {
                if (SetProperty(ref _selectedOutcome, value ?? string.Empty))
                {
                    RefreshView();
                }
            }
        }

        private string _selectedMinimumLevel = string.Empty;
        public string SelectedMinimumLevel
        {
            get => _selectedMinimumLevel;
            set
            {
                if (SetProperty(ref _selectedMinimumLevel, value ?? string.Empty))
                {
                    RefreshView();
                }
            }
        }

        private string _selectedTimeRange = string.Empty;
        public string SelectedTimeRange
        {
            get => _selectedTimeRange;
            set
            {
                if (SetProperty(ref _selectedTimeRange, value ?? string.Empty))
                {
                    RefreshView();
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
                    PendingCount = 0;
                    ReloadSnapshot();
                }
            }
        }

        private int _pendingCount;
        public int PendingCount
        {
            get => _pendingCount;
            private set
            {
                if (SetProperty(ref _pendingCount, value))
                {
                    RaisePropertyChanged(nameof(HasPendingEntries));
                    RefreshPendingText();
                }
            }
        }

        public bool HasPendingEntries => PendingCount > 0;

        private LogEntry _selectedEntry;
        public LogEntry SelectedEntry
        {
            get => _selectedEntry;
            set
            {
                if (SetProperty(ref _selectedEntry, value))
                {
                    RaisePropertyChanged(nameof(HasSelectedEntry));
                }
            }
        }

        public bool HasSelectedEntry => SelectedEntry is not null;

        private int _totalCount;
        public int TotalCount
        {
            get => _totalCount;
            private set => SetProperty(ref _totalCount, value);
        }

        private int _viewCount;
        public int ViewCount
        {
            get => _viewCount;
            private set => SetProperty(ref _viewCount, value);
        }

        private int _filteredCount;
        public int FilteredCount
        {
            get => _filteredCount;
            private set => SetProperty(ref _filteredCount, value);
        }

        private int _operationCount;
        public int OperationCount
        {
            get => _operationCount;
            private set => SetProperty(ref _operationCount, value);
        }

        private int _diagnosticCount;
        public int DiagnosticCount
        {
            get => _diagnosticCount;
            private set => SetProperty(ref _diagnosticCount, value);
        }

        public bool HasVisibleEntries => FilteredCount > 0;

        public bool IsViewEmpty => ViewCount == 0;

        public bool HasNoFilterResults => ViewCount > 0 && FilteredCount == 0;

        public bool HasAnyEntries => TotalCount > 0;

        public bool HasActiveFilters => !string.IsNullOrWhiteSpace(SearchText)
            || !string.IsNullOrWhiteSpace(SelectedCategory)
            || !string.IsNullOrWhiteSpace(SelectedOrigin)
            || IsOperationView && !string.IsNullOrWhiteSpace(SelectedOutcome)
            || IsOperationView && ShowIntermediateOperations
            || !string.IsNullOrWhiteSpace(SelectedMinimumLevel)
            || !string.IsNullOrWhiteSpace(SelectedTimeRange);

        private string _countText;
        public string CountText
        {
            get => _countText;
            private set => SetProperty(ref _countText, value);
        }

        private string _pendingText;
        public string PendingText
        {
            get => _pendingText;
            private set => SetProperty(ref _pendingText, value);
        }

        private string _emptyText;
        public string EmptyText
        {
            get => _emptyText;
            private set => SetProperty(ref _emptyText, value);
        }

        private bool FilterEntry(object item)
        {
            if (item is not LogEntry entry || !MatchesViewMode(entry))
            {
                return false;
            }

            if (!string.IsNullOrWhiteSpace(SelectedCategory)
                && !string.Equals(entry.DisplayCategory, SelectedCategory,
                    StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (!string.IsNullOrWhiteSpace(SelectedOrigin)
                && !string.Equals(entry.Origin, SelectedOrigin,
                    StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (IsOperationView
                && !string.IsNullOrWhiteSpace(SelectedOutcome)
                && !string.Equals(entry.Outcome, SelectedOutcome,
                    StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (!string.IsNullOrWhiteSpace(SelectedMinimumLevel)
                && Enum.TryParse<LogLevel>(SelectedMinimumLevel, true, out var minimumLevel)
                && entry.Level < minimumLevel)
            {
                return false;
            }

            var now = DateTimeOffset.Now;
            if (!MatchesTimeRange(entry, now) || !_searchQuery.Matches(entry, now))
            {
                return false;
            }

            return true;
        }

        private bool MatchesViewMode(LogEntry entry)
        {
            if (IsOperationView)
            {
                return entry.Kind == LogEntryKind.Operation
                    && (ShowIntermediateOperations
                        || !string.Equals(entry.Outcome, "Started",
                            StringComparison.OrdinalIgnoreCase));
            }

            return entry.Kind != LogEntryKind.Operation;
        }

        private bool MatchesTimeRange(LogEntry entry, DateTimeOffset now)
        {
            return SelectedTimeRange switch
            {
                "15m" => entry.Timestamp >= now.AddMinutes(-15),
                "1h" => entry.Timestamp >= now.AddHours(-1),
                "Today" => entry.Timestamp >= new DateTimeOffset(now.Date, now.Offset),
                _ => true,
            };
        }

        private void SetViewMode(LogViewMode mode)
        {
            if (_viewMode == mode)
            {
                return;
            }

            _viewMode = mode;
            RaisePropertyChanged(nameof(IsOperationView));
            RaisePropertyChanged(nameof(IsDiagnosticView));
            SelectedEntry = null;
            RefreshView();
            RefreshEmptyText();
        }

        private void HandleEntryLogged(object sender, LogEntryLoggedEventArgs args)
        {
            if (_isDestroyed || !_loggingControl.IsEnabled || args?.Entry is null)
            {
                return;
            }

            if (IsPaused)
            {
                Dispatch(() => PendingCount++);
                return;
            }

            _pendingEntries.Enqueue(args.Entry);
            ScheduleDrain();
        }

        private void ScheduleDrain()
        {
            if (Interlocked.Exchange(ref _drainScheduled, 1) != 0)
            {
                return;
            }

            Dispatch(DrainPendingEntries);
        }

        private void DrainPendingEntries()
        {
            try
            {
                while (_pendingEntries.TryDequeue(out var entry))
                {
                    _allEntries.Add(entry);
                    EnsureCategoryOption(entry.DisplayCategory);
                    TrimToCapacity();
                }

                UpdateCounts();
            }
            finally
            {
                Interlocked.Exchange(ref _drainScheduled, 0);
                if (!_pendingEntries.IsEmpty)
                {
                    ScheduleDrain();
                }
            }
        }

        private void HandleCleared(object sender, EventArgs e)
        {
            Dispatch(() =>
            {
                while (_pendingEntries.TryDequeue(out _))
                {
                }

                _allEntries.Clear();
                SelectedEntry = null;
                PendingCount = 0;
                UpdateCounts();
            });
        }

        private void HandleLoggingEnabledChanged(object sender, EventArgs e)
        {
            Dispatch(() =>
            {
                RaisePropertyChanged(nameof(IsLoggingEnabled));
                RaisePropertyChanged(nameof(IsLoggingDisabled));
                ReloadSnapshot();
            });
        }

        private void ReloadSnapshot()
        {
            Dispatch(() =>
            {
                while (_pendingEntries.TryDequeue(out _))
                {
                }

                _allEntries.Clear();
                IReadOnlyList<LogEntry> snapshot = _loggingControl.IsEnabled
                    ? _logHistory.GetSnapshot()
                    : [];
                foreach (var entry in snapshot)
                {
                    _allEntries.Add(entry);
                    EnsureCategoryOption(entry.DisplayCategory);
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

        private void RefreshView()
        {
            Entries.Refresh();
            if (SelectedEntry is not null && !Entries.Contains(SelectedEntry))
            {
                SelectedEntry = null;
            }

            UpdateCounts();
            ResetFiltersCommand?.RaiseCanExecuteChanged();
        }

        private void ResetFilters()
        {
            _searchText = string.Empty;
            _selectedCategory = string.Empty;
            _selectedOrigin = string.Empty;
            _selectedOutcome = string.Empty;
            _selectedMinimumLevel = string.Empty;
            _selectedTimeRange = string.Empty;
            _showIntermediateOperations = false;
            _searchQuery = LogSearchQuery.Empty;

            RaisePropertyChanged(nameof(SearchText));
            RaisePropertyChanged(nameof(SelectedCategory));
            RaisePropertyChanged(nameof(SelectedOrigin));
            RaisePropertyChanged(nameof(SelectedOutcome));
            RaisePropertyChanged(nameof(SelectedMinimumLevel));
            RaisePropertyChanged(nameof(SelectedTimeRange));
            RaisePropertyChanged(nameof(ShowIntermediateOperations));
            RefreshView();
        }

        private void UpdateCounts()
        {
            TotalCount = _allEntries.Count;
            OperationCount = _allEntries.Count(entry =>
                entry.Kind == LogEntryKind.Operation
                && !string.Equals(entry.Outcome, "Started", StringComparison.OrdinalIgnoreCase));
            DiagnosticCount = TotalCount - _allEntries.Count(entry =>
                entry.Kind == LogEntryKind.Operation);
            ViewCount = IsOperationView
                ? _allEntries.Count(entry => entry.Kind == LogEntryKind.Operation
                    && (ShowIntermediateOperations
                        || !string.Equals(entry.Outcome, "Started",
                            StringComparison.OrdinalIgnoreCase)))
                : DiagnosticCount;
            FilteredCount = Entries?.Count ?? 0;

            RefreshCountText();
            RefreshEmptyText();
            RaisePropertyChanged(nameof(HasVisibleEntries));
            RaisePropertyChanged(nameof(IsViewEmpty));
            RaisePropertyChanged(nameof(HasNoFilterResults));
            RaisePropertyChanged(nameof(HasAnyEntries));
            RaisePropertyChanged(nameof(HasActiveFilters));
            ResetFiltersCommand?.RaiseCanExecuteChanged();
        }

        private void BuildLocalizedOptions()
        {
            OriginOptions =
            [
                Option(string.Empty, "Setting.Log.Filter.All", "All"),
                Option("Manual", "Setting.Log.Origin.Manual", "Manual"),
                Option("Automation", "Setting.Log.Origin.Automation", "Automation"),
                Option("Observed", "Setting.Log.Origin.Observed", "Observed"),
                Option("System", "Setting.Log.Origin.System", "System"),
            ];
            OutcomeOptions =
            [
                Option(string.Empty, "Setting.Log.Filter.All", "All"),
                Option("Succeeded", "Setting.Log.Outcome.Succeeded", "Succeeded"),
                Option("Failed", "Setting.Log.Outcome.Failed", "Failed"),
                Option("Cancelled", "Setting.Log.Outcome.Cancelled", "Cancelled"),
                Option("Rejected", "Setting.Log.Outcome.Rejected", "Rejected"),
                Option("Skipped", "Setting.Log.Outcome.Skipped", "Skipped"),
            ];
            LevelOptions =
            [
                Option(string.Empty, "Setting.Log.Level.All", "All"),
                Option("Verbose", "Setting.Log.Level.Verbose", "Verbose"),
                Option("Debug", "Setting.Log.Level.Debug", "Debug"),
                Option("Information", "Setting.Log.Level.Information", "Information"),
                Option("Warning", "Setting.Log.Level.Warning", "Warning"),
                Option("Error", "Setting.Log.Level.Error", "Error"),
                Option("Fatal", "Setting.Log.Level.Fatal", "Fatal"),
            ];
            TimeRangeOptions =
            [
                Option(string.Empty, "Setting.Log.Time.All", "Current session"),
                Option("15m", "Setting.Log.Time.15Minutes", "Last 15 minutes"),
                Option("1h", "Setting.Log.Time.1Hour", "Last hour"),
                Option("Today", "Setting.Log.Time.Today", "Today"),
            ];

            RaisePropertyChanged(nameof(OriginOptions));
            RaisePropertyChanged(nameof(OutcomeOptions));
            RaisePropertyChanged(nameof(LevelOptions));
            RaisePropertyChanged(nameof(TimeRangeOptions));
            RebuildCategoryOptions();
        }

        private void RebuildCategoryOptions()
        {
            var categories = _allEntries
                .Select(entry => entry.DisplayCategory)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            _isUpdatingCategoryOptions = true;
            try
            {
                CategoryOptions.Clear();
                CategoryOptions.Add(Option(string.Empty, "Setting.Log.Filter.All", "All"));
                foreach (var category in categories)
                {
                    CategoryOptions.Add(new LogFilterOption(category, LocalizeCategory(category)));
                }
            }
            finally
            {
                _isUpdatingCategoryOptions = false;
            }

            RaisePropertyChanged(nameof(SelectedCategory));
        }

        private void EnsureCategoryOption(string category)
        {
            if (CategoryOptions.Any(option =>
                    string.Equals(option.Value, category, StringComparison.OrdinalIgnoreCase)))
            {
                return;
            }

            CategoryOptions.Add(new LogFilterOption(category, LocalizeCategory(category)));
        }

        private string LocalizeCategory(string category)
        {
            return category switch
            {
                "Application" => FindResource("Setting.Log.Category.Application", "Application"),
                "Automation" => FindResource("Setting.Log.Category.Automation", "Automation"),
                "ChampionSelect" => FindResource("Setting.Log.Category.ChampionSelect",
                    "Champion select"),
                "Diagnostics" => FindResource("Setting.Log.Category.Diagnostics", "Diagnostics"),
                "Match" => FindResource("Setting.Log.Category.Match", "Match"),
                "WebSocket" => FindResource("Setting.Log.Category.WebSocket", "WebSocket"),
                _ => category,
            };
        }

        private LogFilterOption Option(string value, string resourceKey, string fallback)
        {
            return new LogFilterOption(value, FindResource(resourceKey, fallback));
        }

        private string FindResource(string resourceKey, string fallback)
        {
            try
            {
                return ResourceService.FindResource<string>(resourceKey) ?? fallback;
            }
            catch (ResourceReferenceKeyNotFoundException)
            {
                return fallback;
            }
        }

        private void RefreshLocalizedState()
        {
            BuildLocalizedOptions();
            RefreshCountText();
            RefreshPendingText();
            RefreshEmptyText();
        }

        private void RefreshCountText()
        {
            var format = FindResource("Setting.Log.Count", "Showing {0} of {1}");
            CountText = string.Format(format, FilteredCount, ViewCount);
        }

        private void RefreshPendingText()
        {
            var format = FindResource("Setting.Log.Pending", "{0} new entries while paused");
            PendingText = string.Format(format, PendingCount);
        }

        private void RefreshEmptyText()
        {
            EmptyText = IsOperationView
                ? FindResource("Setting.Log.Empty.Operations", "No operation records yet")
                : FindResource("Setting.Log.Empty.Diagnostics", "No diagnostic records yet");
        }

        public override void Destroy()
        {
            _isDestroyed = true;
            _logHistory.EntryLogged -= HandleEntryLogged;
            _logHistory.Cleared -= HandleCleared;
            _loggingControl.EnabledChanged -= HandleLoggingEnabledChanged;
            EventAggregator.GetEvent<LanguageSwitchedEvent>().Unsubscribe(RefreshLocalizedState);
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

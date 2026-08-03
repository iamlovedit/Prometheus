using Prism.Commands;
using Prism.Regions;
using Prometheus.Core;
using Prometheus.Core.Models;
using Prometheus.Core.Mvvm;
using Prometheus.Core.Tasks;
using Prometheus.Services.Interfaces.Client;
using Serilog;
using System.Windows;

namespace Prometheus.Modules.Search.ViewModels
{
    public class SearchViewModel : RegionViewModelBase
    {
        private readonly ISummonerService _summonerService;
        private CancellationTokenSource _searchCts;
        private int _searchVersion;

        public SearchViewModel(IRegionManager regionManager,
            ISummonerService summonerService) : base(regionManager)
        {
            _summonerService = summonerService
                ?? throw new ArgumentNullException(nameof(summonerService));
        }

        private string _searchText;
        public string SearchText
        {
            get { return _searchText; }
            set { SetProperty(ref _searchText, value); }
        }

        private bool _showNotFound;
        public bool ShowNotFound
        {
            get { return _showNotFound; }
            private set { SetProperty(ref _showNotFound, value); }
        }

        private bool _hasResult;
        public bool HasResult
        {
            get { return _hasResult; }
            private set { SetProperty(ref _hasResult, value); }
        }

        private bool _isSearching;
        public bool IsSearching
        {
            get { return _isSearching; }
            private set { SetProperty(ref _isSearching, value); }
        }

        private DelegateCommand _searchCommand;
        public DelegateCommand SearchCommand =>
            _searchCommand ??= new DelegateCommand(ExecuteSearchCommand);

        private void ExecuteSearchCommand()
        {
            SearchAsync(SearchText).Observe("Searching for a summoner");
        }

        private async Task SearchAsync(string riotId)
        {
            if (string.IsNullOrWhiteSpace(riotId))
            {
                return;
            }

            var cancellationTokenSource = new CancellationTokenSource();
            var version = Interlocked.Increment(ref _searchVersion);
            Cancel(Interlocked.Exchange(ref _searchCts,
                cancellationTokenSource));
            var cancellationToken = cancellationTokenSource.Token;

            try
            {
                IsSearching = true;
                ShowNotFound = false;
                HasResult = false;

                var summoner = await _summonerService.SearchSummonerByName(
                    riotId.Trim(), cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                if (version != Volatile.Read(ref _searchVersion))
                {
                    return;
                }

                if (summoner is null)
                {
                    ShowNotFound = true;
                    return;
                }

                ShowSummonerResult(summoner);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
            }
            catch (Exception exception)
            {
                ShowNotFound = true;
                Log.Warning(exception, "Unable to search for a summoner");
            }
            finally
            {
                if (version == Volatile.Read(ref _searchVersion))
                {
                    IsSearching = false;
                }

                Interlocked.CompareExchange(ref _searchCts, null,
                    cancellationTokenSource);
                cancellationTokenSource.Dispose();
            }
        }

        private void ShowSummonerResult(SummonerAccount summoner)
        {
            if (summoner is null)
            {
                return;
            }

            SearchText = summoner.FullName;
            ShowNotFound = false;
            HasResult = true;
            var parameters = new NavigationParameters
            {
                { ParameterNames.Summoner, summoner },
                { ParameterNames.CanEdit, false },
                { ParameterNames.HostRegionName, RegionNames.SearchContent },
                { ParameterNames.ShowPageHeader, false }
            };
            RegionManager.RequestNavigate(RegionNames.SearchContent,
                RegionNames.SummonerDetailView, parameters);
        }

        public override void OnNavigatedTo(NavigationContext navigationContext)
        {
            ShowNotFound = false;
            if (navigationContext.Parameters.TryGetValue<SummonerAccount>(
                    ParameterNames.Summoner, out var summoner) &&
                summoner is not null)
            {
                Dispatch(() => ShowSummonerResult(summoner));
            }
        }

        public override void OnNavigatedFrom(NavigationContext navigationContext)
        {
            CancelPendingSearch();
        }

        public override void Destroy()
        {
            CancelPendingSearch();
            base.Destroy();
        }

        private void CancelPendingSearch()
        {
            Interlocked.Increment(ref _searchVersion);
            Cancel(Interlocked.Exchange(ref _searchCts, null));
            IsSearching = false;
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

        private static void Dispatch(Action action)
        {
            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher is not null)
            {
                dispatcher.BeginInvoke(action);
                return;
            }

            action();
        }
    }
}

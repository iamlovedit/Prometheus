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
using Prometheus.Core.Tasks;
using Prometheus.Services.Interfaces.Client;
using Serilog;
using System.Windows;

namespace Prometheus.Modules.Summoner.ViewModels
{
    public class SummonerViewModel : RegionViewModelBase
    {
        private const string CurrentSummonerUri = "/lol-summoner/v1/current-summoner";

        private SummonerAccount _summoner;
        private readonly ISummonerService _summonerService;
        private readonly IGameResourceManager _gameResourceManager;
        private readonly IEventAggregator _eventAggregator;
        private readonly IDialogService _dialogService;
        private readonly IResourceService _resourceService;
        private readonly ILeagueClient _leagueClient;
        private CancellationTokenSource _loadCts;
        private int _loadVersion;
        private volatile bool _isActive;
        private volatile bool _isViewingCurrentSummoner;
        private readonly static Dictionary<int, (string, string)> _mapsMap = new()
        {
            {-1,("特殊地图","Special map") },
            {11,("召唤师峡谷","Summoner's Rift") },
            {12,("嚎哭深渊","Howling Abyss") },
            {21,("极限闪击","Nexus Blitz") },
            {30,("斗魂竞技场","Arena") },
        };
        public SummonerViewModel(IRegionManager regionManager, IEventAggregator eventAggregator, IContainerExtension containerExtension) : base(regionManager)
        {
            _eventAggregator = eventAggregator;
            _resourceService = containerExtension.Resolve<IResourceService>();
            _summonerService = containerExtension.Resolve<ISummonerService>();
            _gameResourceManager = containerExtension.Resolve<IGameResourceManager>();
            _dialogService = containerExtension.Resolve<IDialogService>();
            _leagueClient = containerExtension.Resolve<ILeagueClient>();

            _eventAggregator.GetEvent<SearchSummonerEvent>().Subscribe(HandleSearchSummoner);
            _leagueClient.Subscribe(CurrentSummonerUri, HandleCurrentSummonerChanged);
        }

        public override void OnNavigatedTo(NavigationContext navigationContext)
        {
            _isActive = true;
            var hasRequestedSummoner = navigationContext.Parameters.TryGetValue<SummonerAccount>(
                ParameterNames.Summoner, out var requestedSummoner) &&
                requestedSummoner is not null;
            _isViewingCurrentSummoner = !hasRequestedSummoner;

            LoadSummonerAsync(requestedSummoner, _isViewingCurrentSummoner)
                .Observe("Loading the summoner page");
        }

        public override void OnNavigatedFrom(NavigationContext navigationContext)
        {
            _isActive = false;
            CancelPendingLoad();
        }

        public override void Destroy()
        {
            _eventAggregator.GetEvent<SearchSummonerEvent>().Unsubscribe(HandleSearchSummoner);
            _leagueClient.Unsubscribe(CurrentSummonerUri, HandleCurrentSummonerChanged);
            CancelPendingLoad();
            base.Destroy();
        }

        private async Task LoadSummonerAsync(SummonerAccount requestedSummoner,
            bool useCurrentSummoner)
        {
            var cancellationTokenSource = new CancellationTokenSource();
            var version = Interlocked.Increment(ref _loadVersion);
            var previousLoad = Interlocked.Exchange(ref _loadCts, cancellationTokenSource);
            Cancel(previousLoad);
            var cancellationToken = cancellationTokenSource.Token;

            try
            {
                var currentSummoner = await _summonerService
                    .GetCurrentSummoner(cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();

                var targetSummoner = useCurrentSummoner
                    ? currentSummoner
                    : requestedSummoner;
                if (targetSummoner is null)
                {
                    return;
                }

                var canEdit = IsSameAccount(targetSummoner, currentSummoner);
                Dispatch(() =>
                {
                    if (!_isActive || version != Volatile.Read(ref _loadVersion))
                    {
                        return;
                    }

                    _summoner = targetSummoner;
                    var parameters = new NavigationParameters
                    {
                        { ParameterNames.Summoner, targetSummoner },
                        { ParameterNames.CanEdit, canEdit }
                    };
                    RegionManager.RequestNavigate(RegionNames.SummonerContent,
                        RegionNames.SummonerDetailView, parameters);
                });
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
            }
            catch (Exception exception)
            {
                Log.Warning(exception, "Unable to refresh the summoner career account");
            }
            finally
            {
                Interlocked.CompareExchange(ref _loadCts, null,
                    cancellationTokenSource);
                cancellationTokenSource.Dispose();
            }
        }

        private void HandleCurrentSummonerChanged(OnWebsocketEventArgs args)
        {
            if (!_isActive || !_isViewingCurrentSummoner)
            {
                return;
            }

            LoadSummonerAsync(null, true)
                .Observe("Refreshing the summoner page after an account change");
        }

        private void HandleSearchSummoner(SummonerAccount summoner)
        {
            _summoner = null;
            _isViewingCurrentSummoner = false;
        }

        private void CancelPendingLoad()
        {
            Interlocked.Increment(ref _loadVersion);
            Cancel(Interlocked.Exchange(ref _loadCts, null));
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

        private static bool IsSameAccount(SummonerAccount left, SummonerAccount right)
        {
            if (left is null || right is null)
            {
                return false;
            }

            return !string.IsNullOrWhiteSpace(left.Puuid) &&
                   !string.IsNullOrWhiteSpace(right.Puuid)
                ? string.Equals(left.Puuid, right.Puuid,
                    StringComparison.OrdinalIgnoreCase)
                : left.SummonerId > 0 && left.SummonerId == right.SummonerId;
        }

        private static void Dispatch(Action action)
        {
            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher is not null && !dispatcher.CheckAccess())
            {
                dispatcher.BeginInvoke(action);
                return;
            }

            action();
        }
    }
}

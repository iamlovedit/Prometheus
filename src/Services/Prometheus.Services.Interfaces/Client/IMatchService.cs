using Prometheus.Core.Models;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Prometheus.Services.Interfaces.Client
{
    public interface IMatchService
    {
        /// <summary>
        /// Gets the latest immutable-at-publication live-match snapshot. Its
        /// <see cref="LiveMatchSnapshot.Version"/> is monotonically increasing.
        /// </summary>
        LiveMatchSnapshot Current { get; }

        event EventHandler<LiveMatchSnapshotChangedEventArgs> SnapshotChanged;

        Task StartAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Manually refreshes the live match, cancelling current roster
        /// enrichment and clearing its successful game/player cache first.
        /// </summary>
        Task RefreshAsync(CancellationToken cancellationToken = default);

        Task StopAsync();

        Task AcceptReadyCheckAsync(CancellationToken cancellationToken = default);

        Task ReconnectAsync(CancellationToken cancellationToken = default);

        IGameAutomationSettings AutomationSettings { get; }
    }
}

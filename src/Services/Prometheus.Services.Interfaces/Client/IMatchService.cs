using Prometheus.Core.Models;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Prometheus.Services.Interfaces.Client
{
    public interface IMatchService
    {
        LiveMatchSnapshot Current { get; }

        event EventHandler<LiveMatchSnapshotChangedEventArgs> SnapshotChanged;

        Task StartAsync(CancellationToken cancellationToken = default);

        Task RefreshAsync(CancellationToken cancellationToken = default);

        Task StopAsync();

        Task AcceptReadyCheckAsync(CancellationToken cancellationToken = default);

        Task ReconnectAsync(CancellationToken cancellationToken = default);

        IGameAutomationSettings AutomationSettings { get; }
    }
}

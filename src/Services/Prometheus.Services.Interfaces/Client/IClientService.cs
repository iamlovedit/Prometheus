
using Prometheus.Core.Models;

namespace Prometheus.Services.Interfaces.Client
{
    public interface IClientService
    {
        Task<string> GetInstallLocation();

        Task QuitClientAsync();

        /// <summary>
        /// Gets queue metadata from <c>lol-game-queues/v1/queues</c>.
        /// Returns an empty list when LCU is unavailable and supports cancellation.
        /// Successful results are cached for the current service lifetime.
        /// </summary>
        Task<IReadOnlyList<GameQueue>> GetQueuesAsync(
            CancellationToken cancellationToken = default);

        Task SetForgeground();

        Task FlashClient();

        Task MinimizeClient();

        Dictionary<string, string> GetClientCommandLines();

        int GetClientProcessId();
    }
}

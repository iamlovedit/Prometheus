namespace Prometheus.Services.Interfaces.Client
{
    public interface ILeagueClientLauncher
    {
        /// <summary>
        /// Returns whether the League client process is currently running.
        /// Process inspection failures are treated as not running.
        /// </summary>
        bool IsLeagueClientRunning();

        /// <summary>
        /// Requests a League of Legends launch through Tencent TCLS for a
        /// supported Chinese installation, or Riot Client for a standalone
        /// installation. WeGame is never started automatically. The method
        /// does not wait for LCU to become available and supports cancellation
        /// while waiting for another launch request to finish.
        /// </summary>
        Task<LeagueClientLaunchStatus> LaunchAsync(
            CancellationToken cancellationToken = default);
    }

    public enum LeagueClientLaunchStatus
    {
        Started,
        AlreadyRunning,
        ExternalLauncherRequired,
        LauncherNotFound,
        Failed
    }
}

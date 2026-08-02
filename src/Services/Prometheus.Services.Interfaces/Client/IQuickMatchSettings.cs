namespace Prometheus.Services.Interfaces.Client
{
    public interface IQuickMatchSettings
    {
        /// <summary>
        /// Raised after the in-memory quick-match queue changes, even when persistence fails.
        /// </summary>
        event EventHandler Changed;

        /// <summary>
        /// Gets the last supported quick-match queue, defaulting to ranked solo/duo.
        /// </summary>
        int QueueId { get; }

        /// <summary>
        /// Saves a supported quick-match queue. Returns whether local persistence succeeded.
        /// </summary>
        bool SaveQueueId(int queueId);
    }
}

namespace Prometheus.Core.Events
{
    /// <summary>
    /// Collects asynchronous shutdown work registered synchronously by application-closing
    /// event subscribers. The window remains open until all registered work has completed.
    /// </summary>
    public sealed class ApplicationShutdownContext
    {
        private readonly object _sync = new();
        private readonly List<Task> _operations = [];
        private Task _completionTask;

        public void Register(Task operation)
        {
            ArgumentNullException.ThrowIfNull(operation);

            lock (_sync)
            {
                if (_completionTask is not null)
                {
                    throw new InvalidOperationException(
                        "Shutdown operations must be registered before waiting begins.");
                }

                _operations.Add(operation);
            }
        }

        public Task WaitForCompletionAsync()
        {
            lock (_sync)
            {
                _completionTask ??= _operations.Count == 0
                    ? Task.CompletedTask
                    : Task.WhenAll(_operations);
                return _completionTask;
            }
        }
    }
}

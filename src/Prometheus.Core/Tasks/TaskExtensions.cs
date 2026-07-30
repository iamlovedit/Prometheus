using Serilog;
using System;
using System.Threading.Tasks;

namespace Prometheus.Core.Tasks
{
    public static class TaskExtensions
    {
        public static void Observe(this Task task, string operation)
        {
            ArgumentNullException.ThrowIfNull(task);
            _ = ObserveAsync(task, operation);
        }

        private static async Task ObserveAsync(Task task, string operation)
        {
            try
            {
                await task.ConfigureAwait(true);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception exception)
            {
                Log.Error(exception, "{Operation} failed", operation);
            }
        }
    }
}

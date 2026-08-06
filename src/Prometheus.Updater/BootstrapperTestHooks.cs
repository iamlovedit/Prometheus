using System.Diagnostics;

namespace Prometheus.Updater;

internal sealed class BootstrapperTestHooks
{
    public Func<string, string, bool>? ValidateDesktopVersion { get; init; }
    public Func<string, string, string?, Process>? StartDesktop { get; init; }
    public Func<Process, string, Task<bool>>? WaitForHealth { get; init; }
}

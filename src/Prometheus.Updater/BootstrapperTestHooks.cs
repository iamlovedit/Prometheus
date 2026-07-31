using System.Diagnostics;
using Prometheus.Update;

namespace Prometheus.Updater;

internal sealed class BootstrapperTestHooks
{
    public Func<SignedEnvelope, ReleaseDescriptor>? VerifyRelease { get; init; }
    public Func<SignedEnvelope, ReleaseManifest>? VerifyManifest { get; init; }
    public Func<string, string, string?, Process>? StartDesktop { get; init; }
    public Func<Process, string, Task<bool>>? WaitForHealth { get; init; }
}

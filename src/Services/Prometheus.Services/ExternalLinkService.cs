#nullable enable

using System.Diagnostics;
using Prometheus.Services.Interfaces;
using Serilog;

namespace Prometheus.Services;

public sealed class ExternalLinkService : IExternalLinkService
{
    private readonly Action<ProcessStartInfo> _startProcess;

    public ExternalLinkService()
        : this(startInfo =>
        {
            _ = Process.Start(startInfo)
                ?? throw new InvalidOperationException("Unable to start the default browser.");
        })
    {
    }

    internal ExternalLinkService(Action<ProcessStartInfo> startProcess)
    {
        _startProcess = startProcess ?? throw new ArgumentNullException(nameof(startProcess));
    }

    public bool Open(Uri uri)
    {
        ArgumentNullException.ThrowIfNull(uri);

        if (!uri.IsAbsoluteUri
            || !string.Equals(uri.Scheme, Uri.UriSchemeHttps,
                StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        try
        {
            _startProcess(new ProcessStartInfo(uri.AbsoluteUri)
            {
                UseShellExecute = true
            });
            return true;
        }
        catch (Exception exception)
        {
            Log.Warning(exception, "Unable to open external link {ExternalUri}", uri);
            return false;
        }
    }
}

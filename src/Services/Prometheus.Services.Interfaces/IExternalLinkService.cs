#nullable enable

namespace Prometheus.Services.Interfaces;

public interface IExternalLinkService
{
    bool Open(Uri uri);
}

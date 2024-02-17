using System.Collections.Generic;

namespace Prometheus.Services.Interfaces
{
    public interface IProcessService
    {
        Dictionary<string, string> GetProcessCommandLines();

        int GetClientProcessId();
    }
}

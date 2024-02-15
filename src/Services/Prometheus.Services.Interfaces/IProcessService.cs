using System;
using System.Collections.Generic;
using System.Text;

namespace Prometheus.Services.Interfaces
{
    public interface IProcessService
    {
        Dictionary<string, string> GetProcessCommandLines();

        int GetClientProcessId();
    }
}

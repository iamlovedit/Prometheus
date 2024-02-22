using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Prometheus.Services.Interfaces.Client
{
    public interface IClientService
    {
        Task<string> GetInstallLocation();

        Task QuitClientAsync();

    }
}

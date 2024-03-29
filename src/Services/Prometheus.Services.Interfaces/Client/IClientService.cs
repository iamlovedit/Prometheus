﻿using System.Collections.Generic;
using System.Threading.Tasks;

namespace Prometheus.Services.Interfaces.Client
{
    public interface IClientService
    {
        Task<string> GetInstallLocation();

        Task QuitClientAsync();

        Task<string> GetQueuesAsync();

        Task SetForgeground();

        Task FlashClient();

        Task MinimizeClient();

        Dictionary<string, string> GetClientCommandLines();

        int GetClientProcessId();
    }
}

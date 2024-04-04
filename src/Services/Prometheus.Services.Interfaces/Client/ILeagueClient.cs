﻿using Prometheus.Core.Models;
using System;

namespace Prometheus.Services.Interfaces.Client
{
    public interface ILeagueClient
    {
        event Action OnConnected;

        event Action OnDisconnected;

        event Action<OnWebsocketEventArgs> OnWebsocketEvent;

        bool Connected { get; }
    }
}

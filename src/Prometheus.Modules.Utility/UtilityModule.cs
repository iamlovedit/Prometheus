﻿using Prism.Ioc;
using Prism.Modularity;
using Prometheus.Core.Models;
using Prometheus.Modules.Utility.Views;

namespace Prometheus.Modules.Utility
{
    public class UtilityModule : IModule
    {
        public void OnInitialized(IContainerProvider containerProvider)
        {

        }

        public void RegisterTypes(IContainerRegistry containerRegistry)
        {
            containerRegistry.RegisterForNavigation<UtilityView>(MenuName.Utility.ToString());
        }
    }
}
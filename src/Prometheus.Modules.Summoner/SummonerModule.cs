﻿using Prism.Ioc;
using Prism.Regions;
using Prometheus.Core;
using Prometheus.Core.Mvvm;
using Prometheus.Modules.Summoner.Views;

namespace Prometheus.Modules.Summoner
{
    public class SummonerModule : ModuleBase
    {
        public SummonerModule(IRegionManager regionManager) : base(regionManager)
        {
        }

        public override void OnInitialized(IContainerProvider containerProvider)
        {
        }

        public override void RegisterTypes(IContainerRegistry containerRegistry)
        {
            containerRegistry.RegisterForNavigation<SummonerView>(RegionNames.CareerView);
            containerRegistry.RegisterDialog<SelectBackgroundDialog>(RegionNames.SelectBackgroundDialog);
        }
    }
}
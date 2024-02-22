using Prism.Commands;
using Prism.Mvvm;
using Prism.Regions;
using Prometheus.Core.Mvvm;
using Prometheus.Services.Interfaces.Client;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Prometheus.Modules.Match.ViewModels
{
    public class MatchViewModel : RegionViewModelBase
    {
        private readonly ISummonerService _summonerService;
        public MatchViewModel(IRegionManager regionManager, ISummonerService summonerService) : base(regionManager)
        {
            _summonerService = summonerService;
        }
    }
}

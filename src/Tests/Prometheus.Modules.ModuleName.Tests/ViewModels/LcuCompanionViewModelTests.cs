using Moq;
using Prism.Events;
using Prometheus.Core.Models;
using Prometheus.Services.Interfaces.Client;
using Prometheus.ViewModels;
using Xunit;

namespace Prometheus.Modules.ModuleName.Tests.ViewModels
{
    public class LcuCompanionViewModelTests
    {
        [Fact]
        public void Start_ProjectsFourTeammatesAndExcludesLocalPlayer()
        {
            var snapshot = new LiveMatchSnapshot
            {
                GameflowPhase = GameflowPhase.ChampSelect,
                GameflowSession = new GameflowSessionSnapshot
                {
                    GameData = new GameflowGameData
                    {
                        QueueId = GameQueueIds.RankedSoloDuo
                    }
                },
                ChampionSelect = new ChampionSelectSnapshot
                {
                    LocalPlayerCellId = 1
                },
                Roster = new LiveMatchRosterSnapshot
                {
                    MyTeam = Enumerable.Range(1, 5)
                        .Select(index => new LiveMatchPlayerSnapshot
                        {
                            CellId = index,
                            IsLocalPlayer = index == 1,
                            DisplayName = index == 1 ? "Local" : $"Teammate {index}",
                            DataState = LiveMatchPlayerDataState.Loaded,
                            RecentMatchCount = 20,
                            RecentWins = 10,
                            RecentLosses = 10
                        })
                        .ToArray()
                }
            };
            var matchService = new Mock<IMatchService>();
            matchService.SetupGet(service => service.Current).Returns(snapshot);
            var automationSettings = new Mock<IGameAutomationSettings>();
            automationSettings.SetupGet(settings => settings.PreferredPickChampionIds)
                .Returns([]);
            automationSettings.SetupGet(settings => settings.PreferredBanChampionIds)
                .Returns([]);
            automationSettings.SetupGet(settings => settings.PreferredAramChampionIds)
                .Returns([]);
            var resourceService = new Mock<IResourceService>();
            resourceService.Setup(service => service.FindResource<string>(
                    It.IsAny<string>()))
                .Returns((string _) => null);
            var viewModel = new LcuCompanionViewModel(
                new EventAggregator(),
                matchService.Object,
                automationSettings.Object,
                new Mock<IGameResourceManager>().Object,
                resourceService.Object);

            viewModel.Start();

            Assert.Equal(4, viewModel.Teammates.Count);
            Assert.DoesNotContain(viewModel.Teammates,
                teammate => teammate.DisplayName == "Local");

            viewModel.Stop();
        }
    }
}

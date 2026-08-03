using Moq;
using Prism.Regions;
using Prometheus.Core.Models;
using Prometheus.Modules.Utility.ViewModels;
using Prometheus.Services.Interfaces.Client;
using System.ComponentModel;
using Xunit;

namespace Prometheus.Modules.ModuleName.Tests.ViewModels
{
    public class UtilityViewModelTests
    {
        [Fact]
        public void Constructor_LoadsSavedProfilePresentation()
        {
            var settings = new Mock<IProfilePresentationSettings>();
            settings.SetupGet(value => value.OnlineStatus).Returns("away");
            settings.SetupGet(value => value.StatusMessage).Returns("Hello");
            settings.SetupGet(value => value.QueueType)
                .Returns(QueueType.RANKED_FLEX_SR);
            settings.SetupGet(value => value.Tier).Returns(Tier.EMERALD);
            settings.SetupGet(value => value.Division).Returns(Division.II);

            var viewModel = CreateViewModel(settings: settings);

            Assert.Equal(1, viewModel.SelectedStatusIndex);
            Assert.Equal("Hello", viewModel.Status);
            Assert.Equal(2, viewModel.SelectedModeIndex);
            Assert.Equal(6, viewModel.SelectedTierIndex);
            Assert.Equal(1, viewModel.SelectedDivisionIndex);
        }

        [Fact]
        public async Task OnlineStatusChange_WhenClientUpdateSucceeds_SavesSelection()
        {
            var gameService = new Mock<IGameService>();
            var settings = new Mock<IProfilePresentationSettings>();
            var saved = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);

            gameService.Setup(service => service.SetOnlineStatusAsync("offline"))
                .Returns(Task.CompletedTask);
            settings.Setup(value => value.SaveOnlineStatus("offline"))
                .Callback(() => saved.TrySetResult(true));
            var viewModel = CreateViewModel(gameService, settings);

            viewModel.SelectedStatusIndex = 2;

            await saved.Task.WaitAsync(TimeSpan.FromSeconds(2));
            gameService.Verify(
                service => service.SetOnlineStatusAsync("offline"), Times.Once);
            settings.Verify(
                value => value.SaveOnlineStatus("offline"), Times.Once);
        }

        [Fact]
        public void PreferredAramChampions_UseConfiguredOrderAndPersistReordering()
        {
            var automationSettings = CreateAutomationSettings();
            var viewModel = CreateViewModel(automationSettings: automationSettings);
            var first = new ChampionSummary { Id = 22, Name = "Ashe" };
            var second = new ChampionSummary { Id = 103, Name = "Ahri" };

            viewModel.SelectedAramChampion = first;
            viewModel.AddAramChampionCommand.Execute();
            viewModel.SelectedAramChampion = second;
            viewModel.AddAramChampionCommand.Execute();
            viewModel.SelectedPreferredAramChampion = second;
            viewModel.MoveAramChampionUpCommand.Execute();

            Assert.Equal(
                [103, 22],
                automationSettings.Object.PreferredAramChampionIds);
        }

        [Fact]
        public void ChampionAutomationEditors_PersistIndependentPriorityOrders()
        {
            var automationSettings = CreateAutomationSettings();
            var viewModel = CreateViewModel(automationSettings: automationSettings);
            var first = new ChampionSummary { Id = 22, Name = "Ashe" };
            var second = new ChampionSummary { Id = 103, Name = "Ahri" };
            var third = new ChampionSummary { Id = 84, Name = "Akali" };
            var champions = new[] { first, second, third };
            viewModel.PickChampionEditor.SetChampionCatalog(champions);
            viewModel.BanChampionEditor.SetChampionCatalog(champions);

            viewModel.PickChampionEditor.SelectedChampion = first;
            viewModel.PickChampionEditor.AddChampionCommand.Execute();
            viewModel.PickChampionEditor.SelectedChampion = second;
            viewModel.PickChampionEditor.AddChampionCommand.Execute();
            viewModel.PickChampionEditor.SelectedPreferredChampion = second;
            viewModel.PickChampionEditor.MoveChampionUpCommand.Execute();

            viewModel.BanChampionEditor.SelectedChampion = third;
            viewModel.BanChampionEditor.AddChampionCommand.Execute();

            Assert.Equal(
                [103, 22],
                automationSettings.Object.PreferredPickChampionIds);
            Assert.Equal(
                [84],
                automationSettings.Object.PreferredBanChampionIds);
        }

        [Fact]
        public void CompanionToggle_UpdatesSharedSetting()
        {
            var companionSettings = CreateCompanionSettings();
            var viewModel = CreateViewModel(companionSettings: companionSettings);
            viewModel.OnNavigatedTo(null);

            Assert.True(viewModel.IsChampionSelectCompanionEnabled);

            viewModel.IsChampionSelectCompanionEnabled = false;

            Assert.False(companionSettings.Object.IsEnabled);

            var changedProperties = new List<string>();
            viewModel.PropertyChanged += (_, args) =>
                changedProperties.Add(args.PropertyName);
            companionSettings.Object.IsEnabled = true;
            companionSettings.Raise(
                settings => settings.PropertyChanged += null,
                new PropertyChangedEventArgs(
                    nameof(ILcuCompanionSettings.IsEnabled)));

            Assert.True(viewModel.IsChampionSelectCompanionEnabled);
            Assert.Contains(
                nameof(UtilityViewModel.IsChampionSelectCompanionEnabled),
                changedProperties);

            viewModel.OnNavigatedFrom(null);
        }

        [Fact]
        public void AramChampionSelector_SelectingFilteredChampion_PreservesSelection()
        {
            var gameResourceManager = new Mock<IGameResourceManager>();
            gameResourceManager.Setup(service => service.GetChampionSummarysAsync())
                .ReturnsAsync(
                [
                    new ChampionSummary { Id = 103, Name = "九尾妖狐", Alias = "Ahri" },
                    new ChampionSummary { Id = 22, Name = "寒冰射手", Alias = "Ashe" }
                ]);
            gameResourceManager.Setup(service =>
                    service.GetChampoinIconByIdAsync(It.IsAny<int>()))
                .ReturnsAsync((int championId) => $"{championId}.png");
            var viewModel = CreateViewModel(gameResourceManager: gameResourceManager);

            viewModel.OnNavigatedTo(null);
            Assert.Null(viewModel.SelectedAramChampion);
            Assert.Equal(2, viewModel.AramChampionOptions.Cast<ChampionSummary>().Count());

            viewModel.AramChampionSearchText = "ahr";
            Assert.Null(viewModel.SelectedAramChampion);
            var champion = Assert.Single(
                viewModel.AramChampionOptions.Cast<ChampionSummary>());
            viewModel.SelectedAramChampion = champion;

            Assert.Same(champion, viewModel.SelectedAramChampion);
            Assert.Equal(champion.Name, viewModel.AramChampionSearchText);
            Assert.True(viewModel.AddAramChampionCommand.CanExecute());
            Assert.Single(viewModel.AramChampionOptions.Cast<ChampionSummary>());
            Assert.Equal(103, champion.Id);
            Assert.Equal("103.png", champion.IconUri);
            gameResourceManager.Verify(service =>
                service.GetChampoinIconByIdAsync(It.IsAny<int>()), Times.Exactly(2));
        }

        [Fact]
        public async Task AramChampionSelector_WhenIconsFinishLoading_DoesNotResetSelection()
        {
            var releaseIcons = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var loadCompleted = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var gameResourceManager = new Mock<IGameResourceManager>();
            gameResourceManager.Setup(service => service.GetChampionSummarysAsync())
                .ReturnsAsync(
                [
                    new ChampionSummary { Id = 103, Name = "九尾妖狐", Alias = "Ahri" },
                    new ChampionSummary { Id = 22, Name = "寒冰射手", Alias = "Ashe" }
                ]);
            gameResourceManager.Setup(service =>
                    service.GetChampoinIconByIdAsync(It.IsAny<int>()))
                .Returns(async (int championId) =>
                {
                    await releaseIcons.Task;
                    return $"{championId}.png";
                });
            var viewModel = CreateViewModel(gameResourceManager: gameResourceManager);

            viewModel.OnNavigatedTo(null);
            Assert.True(viewModel.IsAramChampionListLoading);
            viewModel.AramChampionSearchText = "ahr";
            var champion = Assert.Single(
                viewModel.AramChampionOptions.Cast<ChampionSummary>());
            viewModel.SelectedAramChampion = champion;
            var collectionChangeCount = 0;
            viewModel.AramChampionOptions.CollectionChanged +=
                (_, _) => collectionChangeCount++;
            viewModel.PropertyChanged += (_, eventArgs) =>
            {
                if (eventArgs.PropertyName == nameof(viewModel.IsAramChampionListLoading) &&
                    !viewModel.IsAramChampionListLoading)
                {
                    loadCompleted.TrySetResult(true);
                }
            };

            releaseIcons.TrySetResult(true);
            await loadCompleted.Task.WaitAsync(TimeSpan.FromSeconds(2));

            Assert.Equal(0, collectionChangeCount);
            Assert.Same(champion, viewModel.SelectedAramChampion);
            Assert.True(viewModel.AddAramChampionCommand.CanExecute());
            Assert.Single(viewModel.AramChampionOptions.Cast<ChampionSummary>());
            Assert.All(viewModel.AramChampions,
                value => Assert.Equal($"{value.Id}.png", value.IconUri));
        }

        [Fact]
        public void ChampionSummary_IconUri_RaisesPropertyChanged()
        {
            var champion = new ChampionSummary();
            string changedProperty = null;
            champion.PropertyChanged += (_, eventArgs) =>
                changedProperty = eventArgs.PropertyName;

            champion.IconUri = "22.png";

            Assert.Equal(nameof(ChampionSummary.IconUri), changedProperty);
        }

        [Fact]
        public void AramChampionSelector_WhenInitialLoadIsUnavailable_RetriesWhenOpened()
        {
            var gameResourceManager = new Mock<IGameResourceManager>();
            gameResourceManager.SetupSequence(service => service.GetChampionSummarysAsync())
                .ReturnsAsync((List<ChampionSummary>)null)
                .ReturnsAsync(
                [
                    new ChampionSummary { Id = 22, Name = "寒冰射手", Alias = "Ashe" }
                ]);
            gameResourceManager.Setup(service =>
                    service.GetChampoinIconByIdAsync(22))
                .ReturnsAsync("22.png");
            var viewModel = CreateViewModel(gameResourceManager: gameResourceManager);

            viewModel.OnNavigatedTo(null);
            Assert.Empty(viewModel.AramChampions);

            viewModel.OpenAramChampionSelectorCommand.Execute();

            var champion = Assert.Single(viewModel.AramChampions);
            Assert.Equal(22, champion.Id);
            Assert.Equal("22.png", champion.IconUri);
            gameResourceManager.Verify(service =>
                service.GetChampionSummarysAsync(), Times.Exactly(2));
        }

        private static UtilityViewModel CreateViewModel(
            Mock<IGameService> gameService = null,
            Mock<IProfilePresentationSettings> settings = null,
            Mock<IGameAutomationSettings> automationSettings = null,
            Mock<IGameResourceManager> gameResourceManager = null,
            Mock<ILcuCompanionSettings> companionSettings = null)
        {
            return new UtilityViewModel(
                new Mock<IRegionManager>().Object,
                new Mock<IResourceService>().Object,
                (gameService ?? new Mock<IGameService>()).Object,
                (settings ?? new Mock<IProfilePresentationSettings>()).Object,
                (gameResourceManager ?? new Mock<IGameResourceManager>()).Object,
                (automationSettings ?? CreateAutomationSettings()).Object,
                (companionSettings ?? CreateCompanionSettings()).Object);
        }

        private static Mock<IGameAutomationSettings> CreateAutomationSettings()
        {
            var settings = new Mock<IGameAutomationSettings>();
            settings.SetupProperty(value => value.AutoSwapAramBench, false);
            settings.SetupProperty(value => value.AutoPickChampion, false);
            settings.SetupProperty(value => value.AutoBanChampion, false);
            settings.SetupProperty(
                value => value.PreferredAramChampionIds,
                Array.Empty<int>());
            settings.SetupProperty(
                value => value.PreferredPickChampionIds,
                Array.Empty<int>());
            settings.SetupProperty(
                value => value.PreferredBanChampionIds,
                Array.Empty<int>());
            settings.SetupGet(value => value.LastPersistenceSucceeded).Returns(true);
            return settings;
        }

        private static Mock<ILcuCompanionSettings> CreateCompanionSettings()
        {
            var settings = new Mock<ILcuCompanionSettings>();
            settings.SetupProperty(value => value.IsEnabled, true);
            settings.SetupGet(value => value.LastPersistenceSucceeded).Returns(true);
            return settings;
        }
    }
}

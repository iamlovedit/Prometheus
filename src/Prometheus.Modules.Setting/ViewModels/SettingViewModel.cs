#nullable enable

using HandyControl.Controls;
using Prism.Commands;
using Prism.Regions;
using Prometheus.Core;
using Prometheus.Core.Mvvm;
using Prometheus.Services.Interfaces;
using Prometheus.Services.Interfaces.Client;
using Prometheus.Services.Interfaces.Updates;

namespace Prometheus.Modules.Setting.ViewModels
{
    public class SettingViewModel : ViewModelBase
    {
        private readonly IRegionManager _regionManager;
        private readonly IExternalLinkService _externalLinkService;
        private readonly IResourceService _resourceService;
        private readonly Uri? _githubRepositoryUri;

        public SettingViewModel(
            IRegionManager regionManager,
            IExternalLinkService externalLinkService,
            IResourceService resourceService,
            UpdateServiceOptions updateOptions)
        {
            _regionManager = regionManager;
            _externalLinkService = externalLinkService;
            _resourceService = resourceService;
            _githubRepositoryUri = CreateGitHubRepositoryUri(updateOptions);
            OpenOverviewCommand = new DelegateCommand(() =>
                Navigate(RegionNames.SettingPreferenceView, false));
            OpenDiagnosticsCommand = new DelegateCommand(() =>
                Navigate(RegionNames.SettingLogView, true));
            OpenGitHubCommand = new DelegateCommand(OpenGitHub,
                () => _githubRepositoryUri is not null);
        }

        public DelegateCommand OpenOverviewCommand { get; }

        public DelegateCommand OpenDiagnosticsCommand { get; }

        public DelegateCommand OpenGitHubCommand { get; }

        private bool _isDiagnosticsOpen;
        public bool IsDiagnosticsOpen
        {
            get => _isDiagnosticsOpen;
            private set => SetProperty(ref _isDiagnosticsOpen, value);
        }

        private void Navigate(string target, bool isDiagnosticsOpen)
        {
            _regionManager.RequestNavigate(
                RegionNames.SettingContentRegion,
                target,
                result =>
                {
                    if (result.Result == true)
                    {
                        IsDiagnosticsOpen = isDiagnosticsOpen;
                    }
                });
        }

        private void OpenGitHub()
        {
            if (_githubRepositoryUri is null
                || _externalLinkService.Open(_githubRepositoryUri))
            {
                return;
            }

            Growl.Error(_resourceService.FindResource<string>(
                "Setting.GitHub.OpenFailed"));
        }

        private static Uri? CreateGitHubRepositoryUri(UpdateServiceOptions updateOptions)
        {
            ArgumentNullException.ThrowIfNull(updateOptions);

            if (string.IsNullOrWhiteSpace(updateOptions.GitHubOwner)
                || string.IsNullOrWhiteSpace(updateOptions.GitHubRepository))
            {
                return null;
            }

            return new Uri($"https://github.com/"
                + $"{Uri.EscapeDataString(updateOptions.GitHubOwner)}/"
                + Uri.EscapeDataString(updateOptions.GitHubRepository));
        }
    }
}

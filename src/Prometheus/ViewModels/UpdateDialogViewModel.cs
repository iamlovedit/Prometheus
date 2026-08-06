#nullable enable

using Prism.Commands;
using Prism.Events;
using Prism.Mvvm;
using Prism.Services.Dialogs;
using Prometheus.Core.Events;
using Prometheus.Services.Interfaces.Client;
using Prometheus.Services.Interfaces.Updates;
using System.Windows;

namespace Prometheus.ViewModels;

public sealed class UpdateDialogViewModel : BindableBase, IDialogAware
{
    private readonly IUpdateService _updateService;
    private readonly IEventAggregator _eventAggregator;
    private readonly IResourceService _resourceService;
    private CancellationTokenSource? _downloadCancellation;
    private bool _allowClose;

    public UpdateDialogViewModel(IUpdateService updateService, IEventAggregator eventAggregator,
        IResourceService resourceService)
    {
        _updateService = updateService;
        _eventAggregator = eventAggregator;
        _resourceService = resourceService;
        InstallCommand = new DelegateCommand(Install, () => !IsBusy);
        LaterCommand = new DelegateCommand(CloseLater, () => !IsMandatory && !IsBusy);
        CancelCommand = new DelegateCommand(CancelDownload, () => CanCancelDownload);
        ExitCommand = new DelegateCommand(ExitApplication, () => IsMandatory && !IsBusy);
        _updateService.StateChanged += HandleStateChanged;
        RefreshState();
    }

    public string Title => Text("Update.Dialog.Title");
    public DelegateCommand InstallCommand { get; }
    public DelegateCommand LaterCommand { get; }
    public DelegateCommand CancelCommand { get; }
    public DelegateCommand ExitCommand { get; }

    private string _version = string.Empty;
    public string Version
    {
        get => _version;
        private set => SetProperty(ref _version, value);
    }

    private string _releaseNotes = string.Empty;
    public string ReleaseNotes
    {
        get => _releaseNotes;
        private set => SetProperty(ref _releaseNotes, value);
    }

    private string _statusText = string.Empty;
    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

    private string? _errorMessage;
    public string? ErrorMessage
    {
        get => _errorMessage;
        private set => SetProperty(ref _errorMessage, value);
    }

    private double _progress;
    public double Progress
    {
        get => _progress;
        private set => SetProperty(ref _progress, value);
    }

    private bool _isProgressVisible;
    public bool IsProgressVisible
    {
        get => _isProgressVisible;
        private set => SetProperty(ref _isProgressVisible, value);
    }

    private bool _isBusy;
    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetProperty(ref _isBusy, value))
            {
                RaiseCommandStates();
            }
        }
    }

    private bool _isMandatory;
    public bool IsMandatory
    {
        get => _isMandatory;
        private set
        {
            if (SetProperty(ref _isMandatory, value))
            {
                RaiseCommandStates();
            }
        }
    }

    private bool _canCancelDownload;
    public bool CanCancelDownload
    {
        get => _canCancelDownload;
        private set
        {
            if (SetProperty(ref _canCancelDownload, value))
            {
                RaiseCommandStates();
            }
        }
    }

    public event Action<IDialogResult>? RequestClose;

    public bool CanCloseDialog() => _allowClose || (!IsMandatory && !IsBusy);

    public void OnDialogClosed()
    {
        _updateService.StateChanged -= HandleStateChanged;
    }

    public void OnDialogOpened(IDialogParameters parameters)
    {
        RefreshState();
    }

    private async void Install()
    {
        try
        {
            if (_updateService.State is UpdateState.Available or UpdateState.Failed)
            {
                using var cancellation = new CancellationTokenSource();
                _downloadCancellation = cancellation;
                try
                {
                    await _updateService.DownloadAsync(cancellation.Token);
                }
                finally
                {
                    _downloadCancellation = null;
                }
            }
            if (_updateService.State == UpdateState.ReadyToInstall)
            {
                await _updateService.InstallAsync();
                _allowClose = true;
                RequestClose?.Invoke(new DialogResult(ButtonResult.OK));
                _eventAggregator.GetEvent<ApplicationExitRequestedEvent>().Publish();
            }
        }
        catch (OperationCanceledException)
        {
            RefreshState();
        }
        catch
        {
            RefreshState();
        }
    }

    private void CancelDownload()
    {
        _downloadCancellation?.Cancel();
    }

    private void CloseLater()
    {
        _allowClose = true;
        RequestClose?.Invoke(new DialogResult(ButtonResult.Cancel));
    }

    private void ExitApplication()
    {
        _allowClose = true;
        RequestClose?.Invoke(new DialogResult(ButtonResult.Abort));
        _eventAggregator.GetEvent<ApplicationExitRequestedEvent>().Publish();
    }

    private void HandleStateChanged(object? sender, UpdateStateChangedEventArgs args)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
        {
            RefreshState();
        }
        else
        {
            dispatcher.BeginInvoke(RefreshState);
        }
    }

    private void RefreshState()
    {
        var update = _updateService.AvailableUpdate;
        Version = update?.Version ?? string.Empty;
        ReleaseNotes = update?.ReleaseNotes ?? string.Empty;
        IsMandatory = update?.IsMandatory == true;
        Progress = _updateService.Progress * 100;
        ErrorMessage = _updateService.ErrorMessage;
        IsBusy = _updateService.State is UpdateState.Downloading
            or UpdateState.Verifying or UpdateState.Installing;
        CanCancelDownload = _updateService.State is UpdateState.Downloading
            or UpdateState.Verifying;
        IsProgressVisible = _updateService.State is UpdateState.Downloading
            or UpdateState.Verifying or UpdateState.ReadyToInstall or UpdateState.Installing;
        StatusText = Text(_updateService.State switch
        {
            UpdateState.Downloading => "Update.Status.Downloading",
            UpdateState.Verifying => "Update.Status.Verifying",
            UpdateState.ReadyToInstall => "Update.Status.Ready",
            UpdateState.Installing => "Update.Status.Installing",
            UpdateState.Failed => "Update.Status.Failed",
            _ => "Update.Status.Available"
        });
    }

    private void RaiseCommandStates()
    {
        InstallCommand.RaiseCanExecuteChanged();
        LaterCommand.RaiseCanExecuteChanged();
        CancelCommand.RaiseCanExecuteChanged();
        ExitCommand.RaiseCanExecuteChanged();
    }

    private string Text(string key)
    {
        return _resourceService.FindResource<string>(key) ?? key;
    }
}

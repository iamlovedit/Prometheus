#nullable enable

using Prometheus.Update;

namespace Prometheus.Services.Interfaces.Updates;

public enum UpdateState
{
    Idle,
    Checking,
    UpToDate,
    Available,
    Downloading,
    ReadyToInstall,
    Installing,
    Failed
}

public sealed class AvailableUpdate
{
    public ReleaseDescriptor Descriptor { get; init; } = new();
    public UpdateApiResponse ApiResponse { get; init; } = new();
    public bool IsMandatory { get; init; }
    public string ReleaseNotes { get; init; } = string.Empty;
    public long DownloadSize { get; init; }
}

public sealed class UpdateStateChangedEventArgs : EventArgs
{
    public UpdateStateChangedEventArgs(UpdateState state, double progress, string? errorMessage)
    {
        State = state;
        Progress = progress;
        ErrorMessage = errorMessage;
    }

    public UpdateState State { get; }
    public double Progress { get; }
    public string? ErrorMessage { get; }
}

public sealed class UpdateServiceOptions
{
    public string ApiBaseUrl { get; init; } = string.Empty;
    public string InstallRoot { get; init; } = string.Empty;
    public string CurrentVersion { get; init; } = string.Empty;
    public string BootstrapperVersion { get; init; } = "1.0.0";
    public string LocalDataRoot { get; init; } = string.Empty;
}

public interface IUpdateService
{
    UpdateState State { get; }
    double Progress { get; }
    string? ErrorMessage { get; }
    AvailableUpdate? AvailableUpdate { get; }
    event EventHandler<UpdateStateChangedEventArgs>? StateChanged;

    Task<AvailableUpdate?> CheckAsync(bool manual,
        CancellationToken cancellationToken = default);
    Task DownloadAsync(CancellationToken cancellationToken = default);
    Task InstallAsync(CancellationToken cancellationToken = default);
}

using Prometheus.Core.Models;
using Prometheus.Core.Mvvm;
using Prometheus.Services.Interfaces.Client;
using Prometheus.ViewModels;
using Prometheus.Views;
using Serilog;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;

namespace Prometheus.Desktop.Services
{
    public interface ILcuCompanionWindowController
    {
        void Start();

        void Stop();
    }

    public sealed class LcuCompanionWindowController : ILcuCompanionWindowController
    {
        private const uint NoActivate = 0x0010;
        private const uint NoZOrder = 0x0004;
        private const uint ShowWindowFlag = 0x0040;
        private const uint NoOwnerZOrder = 0x0200;
        private const uint PreviousWindowCommand = 3;
        private const int ExtendedStyleIndex = -20;
        private const long TopmostStyle = 0x00000008L;
        private const int HealthCheckIntervalMilliseconds = 300;

        private readonly ILcuWindowTracker _windowTracker;
        private readonly IMatchService _matchService;
        private readonly ILcuCompanionSettings _settings;
        private readonly LcuCompanionWindow _window;
        private readonly LcuCompanionViewModel _viewModel;
        private readonly LatestValueDispatcher<byte> _updateDispatcher;
        private readonly DispatcherTimer _positionRetryTimer;
        private readonly DispatcherTimer _healthTimer;
        private LiveMatchSnapshot _snapshot = LiveMatchSnapshot.Empty;
        private bool _mainWindowHiddenForPhase;
        private bool _started;
        private bool _windowClosed;

        public LcuCompanionWindowController(
            ILcuWindowTracker windowTracker,
            IMatchService matchService,
            ILcuCompanionSettings settings,
            LcuCompanionWindow window,
            LcuCompanionViewModel viewModel)
        {
            _windowTracker = windowTracker ??
                throw new ArgumentNullException(nameof(windowTracker));
            _matchService = matchService ?? throw new ArgumentNullException(nameof(matchService));
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _window = window ?? throw new ArgumentNullException(nameof(window));
            _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
            _updateDispatcher = new LatestValueDispatcher<byte>(
                action => Dispatch(action),
                _ => UpdateWindow());
            _positionRetryTimer = new DispatcherTimer(DispatcherPriority.Background)
            {
                Interval = TimeSpan.FromMilliseconds(250)
            };
            _positionRetryTimer.Tick += HandlePositionRetryTimerTick;
            _healthTimer = new DispatcherTimer(DispatcherPriority.Background)
            {
                Interval = TimeSpan.FromMilliseconds(HealthCheckIntervalMilliseconds)
            };
            _healthTimer.Tick += HandleHealthTimerTick;
        }

        public void Start()
        {
            if (_started || _windowClosed)
            {
                return;
            }

            _started = true;
            _snapshot = _matchService.Current ?? LiveMatchSnapshot.Empty;
            _matchService.SnapshotChanged += HandleSnapshotChanged;
            _settings.PropertyChanged += HandleSettingsPropertyChanged;
            _windowTracker.StateChanged += HandleWindowStateChanged;
            _viewModel.Start();
            _windowTracker.Start();
            _healthTimer.Start();
            QueueUpdate();
        }

        public void Stop()
        {
            if (!_started && _windowClosed)
            {
                return;
            }

            if (_started)
            {
                _started = false;
                _positionRetryTimer.Stop();
                _healthTimer.Stop();
                _matchService.SnapshotChanged -= HandleSnapshotChanged;
                _settings.PropertyChanged -= HandleSettingsPropertyChanged;
                _windowTracker.StateChanged -= HandleWindowStateChanged;
                _windowTracker.Stop();
                _viewModel.Stop();
            }

            Dispatch(() =>
            {
                if (_windowClosed)
                {
                    return;
                }

                _windowClosed = true;
                _window.Close();
            });
        }

        private void HandleSnapshotChanged(
            object sender,
            LiveMatchSnapshotChangedEventArgs args)
        {
            _snapshot = args?.Snapshot ?? LiveMatchSnapshot.Empty;
            QueueUpdate();
        }

        private void HandleWindowStateChanged(
            object sender,
            LcuWindowStateChangedEventArgs args)
        {
            QueueUpdate();
        }

        private void HandleSettingsPropertyChanged(
            object sender,
            PropertyChangedEventArgs args)
        {
            if (string.IsNullOrEmpty(args?.PropertyName) ||
                args.PropertyName == nameof(ILcuCompanionSettings.IsEnabled))
            {
                QueueUpdate();
            }
        }

        private void HandlePositionRetryTimerTick(object sender, EventArgs args)
        {
            _positionRetryTimer.Stop();
            QueueUpdate();
        }

        private void HandleHealthTimerTick(object sender, EventArgs args)
        {
            if (!_started || _windowClosed ||
                _snapshot.GameflowPhase != GameflowPhase.ChampSelect ||
                !_settings.IsEnabled)
            {
                return;
            }

            var state = _windowTracker.Current;
            if (state?.IsAvailable == true && state.IsVisible && !state.IsMinimized &&
                NeedsWindowRefresh(state))
            {
                QueueUpdate();
            }
        }

        private void QueueUpdate()
        {
            _updateDispatcher.Publish(0);
        }

        private bool NeedsWindowRefresh(LcuWindowState state)
        {
            if (!_window.IsVisible)
            {
                return true;
            }

            var companionHandle = new WindowInteropHelper(_window).Handle;
            return companionHandle == IntPtr.Zero ||
                !IsWindowVisible(companionHandle) ||
                !LcuCompanionZOrderCalculator.Calculate(
                    state.Handle,
                    companionHandle,
                    window => GetWindow(window, PreviousWindowCommand),
                    IsTopmostWindow).PreserveCurrent;
        }

        private void UpdateWindow()
        {
            if (!_started || _windowClosed)
            {
                return;
            }

            var isChampionSelect =
                _snapshot.GameflowPhase == GameflowPhase.ChampSelect;
            if (!isChampionSelect)
            {
                _mainWindowHiddenForPhase = false;
                HideWindow();
                return;
            }

            if (!_settings.IsEnabled)
            {
                HideWindow();
                return;
            }

            var state = _windowTracker.Current;
            if (state?.IsAvailable != true || !state.IsVisible || state.IsMinimized)
            {
                HideWindow();
                return;
            }

            try
            {
                var placement = LcuCompanionPlacementCalculator.Calculate(state);
                var dpi = state.Dpi > 0 ? state.Dpi : 96;
                _window.Width = placement.Width * 96d / dpi;
                _window.Height = placement.Height * 96d / dpi;
                _window.ApplyPlacementSide(placement.Side);
                if (!_window.IsVisible)
                {
                    _window.Show();
                }

                var handle = new WindowInteropHelper(_window).Handle;
                var zOrder = LcuCompanionZOrderCalculator.Calculate(
                    state.Handle,
                    handle,
                    window => GetWindow(window, PreviousWindowCommand),
                    IsTopmostWindow);
                var flags = NoActivate | ShowWindowFlag | NoOwnerZOrder;
                if (zOrder.PreserveCurrent)
                {
                    flags |= NoZOrder;
                }

                if (!SetWindowPos(
                        handle,
                        zOrder.InsertAfter,
                        placement.Left,
                        placement.Top,
                        placement.Width,
                        placement.Height,
                        flags))
                {
                    HideWindow();
                    SchedulePositionRetry();
                    return;
                }

                _positionRetryTimer.Stop();

                if (!_mainWindowHiddenForPhase)
                {
                    var mainWindow = Application.Current?.MainWindow;
                    if (mainWindow is not null && mainWindow != _window &&
                        mainWindow.IsVisible)
                    {
                        mainWindow.Hide();
                    }

                    _mainWindowHiddenForPhase = true;
                }
            }
            catch (Exception exception)
            {
                Log.Warning(exception,
                    "Unable to position the LCU champion-select companion window");
                HideWindow();
                SchedulePositionRetry();
            }
        }

        private void SchedulePositionRetry()
        {
            if (_started && !_windowClosed)
            {
                _positionRetryTimer.Stop();
                _positionRetryTimer.Start();
            }
        }

        private void HideWindow()
        {
            if (_window.IsVisible)
            {
                _window.Hide();
            }
        }

        private static void Dispatch(Action action)
        {
            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher is null || dispatcher.CheckAccess())
            {
                action();
                return;
            }

            dispatcher.BeginInvoke(action);
        }

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetWindowPos(
            IntPtr window,
            IntPtr insertAfter,
            int left,
            int top,
            int width,
            int height,
            uint flags);

        [DllImport("user32.dll")]
        private static extern IntPtr GetWindow(
            IntPtr window,
            uint command);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool IsWindowVisible(IntPtr window);

        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
        private static extern IntPtr GetWindowLongPtr(
            IntPtr window,
            int index);

        private static bool IsTopmostWindow(IntPtr window)
        {
            if (window == IntPtr.Zero)
            {
                return false;
            }

            SetLastError(0);
            var style = GetWindowLongPtr(window, ExtendedStyleIndex);
            if (style == IntPtr.Zero && Marshal.GetLastWin32Error() != 0)
            {
                return true;
            }

            return (style.ToInt64() & TopmostStyle) != 0;
        }

        [DllImport("kernel32.dll", SetLastError = false)]
        private static extern void SetLastError(uint errorCode);
    }
}

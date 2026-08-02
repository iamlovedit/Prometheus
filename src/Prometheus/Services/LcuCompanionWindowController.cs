using Prometheus.Core.Models;
using Prometheus.Services.Interfaces.Client;
using Prometheus.ViewModels;
using Prometheus.Views;
using Serilog;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

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

        private readonly ILcuWindowTracker _windowTracker;
        private readonly IMatchService _matchService;
        private readonly LcuCompanionWindow _window;
        private readonly LcuCompanionViewModel _viewModel;
        private LiveMatchSnapshot _snapshot = LiveMatchSnapshot.Empty;
        private bool _mainWindowHiddenForPhase;
        private bool _started;
        private bool _windowClosed;

        public LcuCompanionWindowController(
            ILcuWindowTracker windowTracker,
            IMatchService matchService,
            LcuCompanionWindow window,
            LcuCompanionViewModel viewModel)
        {
            _windowTracker = windowTracker ??
                throw new ArgumentNullException(nameof(windowTracker));
            _matchService = matchService ?? throw new ArgumentNullException(nameof(matchService));
            _window = window ?? throw new ArgumentNullException(nameof(window));
            _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
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
            _windowTracker.StateChanged += HandleWindowStateChanged;
            _viewModel.Start();
            _windowTracker.Start();
            Dispatch(UpdateWindow);
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
                _matchService.SnapshotChanged -= HandleSnapshotChanged;
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
            Dispatch(UpdateWindow);
        }

        private void HandleWindowStateChanged(
            object sender,
            LcuWindowStateChangedEventArgs args)
        {
            Dispatch(UpdateWindow);
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
                if (!_window.IsVisible)
                {
                    _window.Show();
                }

                var handle = new WindowInteropHelper(_window).Handle;
                var flags = NoActivate | ShowWindowFlag | NoOwnerZOrder;
                if (!state.IsForeground)
                {
                    flags |= NoZOrder;
                }

                if (!SetWindowPos(
                        handle,
                        IntPtr.Zero,
                        placement.Left,
                        placement.Top,
                        placement.Width,
                        placement.Height,
                        flags))
                {
                    HideWindow();
                    return;
                }

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
    }
}

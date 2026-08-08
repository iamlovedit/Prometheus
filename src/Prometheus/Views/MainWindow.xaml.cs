using HandyControl.Controls;
using Prism.Events;
using Prometheus.Core.Events;
using Serilog;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using System.Windows.Threading;

namespace Prometheus.Views
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private const int SwRestore = 9;
        private const uint FlashwTray = 0x00000002;
        private const uint ForegroundFlashCount = 3;

        private readonly IEventAggregator _eventAggregator;
        private bool _isExitRequested;
        private bool _shutdownInProgress;
        private bool _shutdownCompleted;

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool ShowWindowAsync(IntPtr windowHandle, int command);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetForegroundWindow(IntPtr windowHandle);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool FlashWindowEx(ref FlashWindowInfo flashInfo);

        public MainWindow(IEventAggregator eventAggregator)
        {
            InitializeComponent();
            _eventAggregator = eventAggregator;
            Closing += MainWindow_Closing;
            Closed += MainWindow_Closed;
            Loaded += MainWindow_Loaded;
            _eventAggregator.GetEvent<ApplicationExitRequestedEvent>()
                .Subscribe(HandleApplicationExitRequested);
            _eventAggregator.GetEvent<ShowMainWindowEvent>()
                .Subscribe(ShowMainWindow);
        }

        private async void MainWindow_Closing(object sender, CancelEventArgs e)
        {
            if (!_isExitRequested)
            {
                e.Cancel = true;
                Hide();
                return;
            }

            if (_shutdownCompleted)
            {
                return;
            }

            e.Cancel = true;
            if (_shutdownInProgress)
            {
                return;
            }

            _shutdownInProgress = true;
            var shutdownContext = new ApplicationShutdownContext();
            try
            {
                _eventAggregator.GetEvent<WindowClosingEvent>().Publish(shutdownContext);
                await shutdownContext.WaitForCompletionAsync();
            }
            catch (Exception exception)
            {
                Log.Warning(exception, "Unable to complete application shutdown cleanly");
            }
            finally
            {
                _shutdownCompleted = true;
                _shutdownInProgress = false;
                _ = Dispatcher.BeginInvoke(new Action(Close), DispatcherPriority.Normal);
            }
        }

        private void TrayIcon_MouseDoubleClick(object sender, System.Windows.RoutedEventArgs e)
        {
            ShowMainWindow();
        }

        private void ExitApplication_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            _isExitRequested = true;
            Close();
        }

        private void MainWindow_Closed(object sender, EventArgs e)
        {
            _eventAggregator.GetEvent<ApplicationExitRequestedEvent>()
                .Unsubscribe(HandleApplicationExitRequested);
            _eventAggregator.GetEvent<ShowMainWindowEvent>()
                .Unsubscribe(ShowMainWindow);
            TrayIcon.Dispose();
        }

        private static void MainWindow_Loaded(object sender, System.Windows.RoutedEventArgs e)
        {
            UpdateRuntime.MarkHealthReady();
        }

        private void HandleApplicationExitRequested()
        {
            Dispatcher.Invoke(() =>
            {
                _isExitRequested = true;
                Close();
            });
        }

        private void ShowMainWindow()
        {
            try
            {
                if (!IsVisible)
                {
                    Show();
                }

                if (WindowState == System.Windows.WindowState.Minimized)
                {
                    WindowState = System.Windows.WindowState.Normal;
                }

                var windowHandle = new WindowInteropHelper(this).EnsureHandle();
                if (windowHandle == IntPtr.Zero)
                {
                    Activate();
                    return;
                }

                _ = ShowWindowAsync(windowHandle, SwRestore);
                if (Activate() || SetForegroundWindow(windowHandle))
                {
                    return;
                }

                var flashInfo = new FlashWindowInfo
                {
                    Size = (uint)Marshal.SizeOf<FlashWindowInfo>(),
                    WindowHandle = windowHandle,
                    Flags = FlashwTray,
                    Count = ForegroundFlashCount,
                    Timeout = 0
                };
                _ = FlashWindowEx(ref flashInfo);
            }
            catch (Exception exception)
            {
                Log.Debug(exception,
                    "Unable to restore or activate the Prometheus main window");
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct FlashWindowInfo
        {
            public uint Size;

            public IntPtr WindowHandle;

            public uint Flags;

            public uint Count;

            public uint Timeout;
        }
    }
}

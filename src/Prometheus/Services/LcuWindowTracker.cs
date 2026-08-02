using Prometheus.Services.Interfaces.Client;
using Serilog;
using System.Runtime.InteropServices;
using System.Windows.Threading;

namespace Prometheus.Desktop.Services
{
    public readonly record struct NativeWindowBounds(
        int Left,
        int Top,
        int Right,
        int Bottom)
    {
        public int Width => Math.Max(0, Right - Left);

        public int Height => Math.Max(0, Bottom - Top);

        public bool IsEmpty => Width == 0 || Height == 0;
    }

    public sealed record LcuWindowState(
        IntPtr Handle,
        NativeWindowBounds Bounds,
        NativeWindowBounds WorkArea,
        int Dpi,
        bool IsVisible,
        bool IsMinimized,
        bool IsForeground)
    {
        public static LcuWindowState Unavailable { get; } = new(
            IntPtr.Zero,
            default,
            default,
            96,
            false,
            false,
            false);

        public bool IsAvailable => Handle != IntPtr.Zero && !Bounds.IsEmpty;
    }

    public sealed class LcuWindowStateChangedEventArgs : EventArgs
    {
        public LcuWindowStateChangedEventArgs(LcuWindowState state)
        {
            State = state ?? throw new ArgumentNullException(nameof(state));
        }

        public LcuWindowState State { get; }
    }

    public interface ILcuWindowTracker : IDisposable
    {
        LcuWindowState Current { get; }

        event EventHandler<LcuWindowStateChangedEventArgs> StateChanged;

        void Start();

        void Stop();
    }

    public sealed class LcuWindowTracker : ILcuWindowTracker
    {
        private const uint DwmExtendedFrameBounds = 9;
        private const uint DwmCloaked = 14;
        private const uint MonitorDefaultToNearest = 2;

        private readonly ILeagueClient _leagueClient;
        private readonly DispatcherTimer _timer;
        private bool _started;
        private bool _disposed;

        public LcuWindowTracker(ILeagueClient leagueClient)
        {
            _leagueClient = leagueClient ??
                throw new ArgumentNullException(nameof(leagueClient));
            _timer = new DispatcherTimer(DispatcherPriority.Background)
            {
                Interval = TimeSpan.FromMilliseconds(250)
            };
            _timer.Tick += HandleTimerTick;
        }

        public LcuWindowState Current { get; private set; } =
            LcuWindowState.Unavailable;

        public event EventHandler<LcuWindowStateChangedEventArgs> StateChanged;

        public void Start()
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_started)
            {
                return;
            }

            _started = true;
            Poll();
            _timer.Start();
        }

        public void Stop()
        {
            if (!_started)
            {
                return;
            }

            _started = false;
            _timer.Stop();
            Publish(LcuWindowState.Unavailable);
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            Stop();
            _timer.Tick -= HandleTimerTick;
            _disposed = true;
        }

        private void HandleTimerTick(object sender, EventArgs args)
        {
            Poll();
        }

        private void Poll()
        {
            if (!_started)
            {
                return;
            }

            try
            {
                var processId = _leagueClient.ProcessId;
                if (processId <= 0)
                {
                    Publish(LcuWindowState.Unavailable);
                    return;
                }

                var handle = FindMainWindow(processId);
                if (handle == IntPtr.Zero || !TryGetBounds(handle, out var bounds))
                {
                    Publish(LcuWindowState.Unavailable);
                    return;
                }

                var monitor = MonitorFromWindow(handle, MonitorDefaultToNearest);
                var monitorInfo = new MonitorInfo
                {
                    Size = Marshal.SizeOf<MonitorInfo>()
                };
                var workArea = GetMonitorInfo(monitor, ref monitorInfo)
                    ? monitorInfo.WorkArea.ToBounds()
                    : bounds;
                var dpi = (int)GetDpiForWindow(handle);
                if (dpi <= 0)
                {
                    dpi = 96;
                }

                var foregroundHandle = GetForegroundWindow();
                GetWindowThreadProcessId(foregroundHandle, out var foregroundProcessId);
                var isCloaked = TryGetCloaked(handle);
                Publish(new LcuWindowState(
                    handle,
                    bounds,
                    workArea,
                    dpi,
                    IsWindowVisible(handle) && !isCloaked,
                    IsIconic(handle),
                    foregroundProcessId == processId));
            }
            catch (Exception exception)
            {
                Log.Debug(exception, "Unable to inspect the League client window");
                Publish(LcuWindowState.Unavailable);
            }
        }

        private void Publish(LcuWindowState state)
        {
            state ??= LcuWindowState.Unavailable;
            if (Equals(Current, state))
            {
                return;
            }

            Current = state;
            StateChanged?.Invoke(this, new LcuWindowStateChangedEventArgs(state));
        }

        private static IntPtr FindMainWindow(int processId)
        {
            var bestHandle = IntPtr.Zero;
            var bestArea = 0L;
            EnumWindows((handle, _) =>
            {
                GetWindowThreadProcessId(handle, out var candidateProcessId);
                if (candidateProcessId != processId || !IsWindowVisible(handle) ||
                    !TryGetBounds(handle, out var bounds) ||
                    bounds.Width < 400 || bounds.Height < 300)
                {
                    return true;
                }

                var area = (long)bounds.Width * bounds.Height;
                if (area > bestArea)
                {
                    bestArea = area;
                    bestHandle = handle;
                }

                return true;
            }, IntPtr.Zero);
            return bestHandle;
        }

        private static bool TryGetBounds(IntPtr handle, out NativeWindowBounds bounds)
        {
            if (DwmGetWindowAttribute(
                    handle,
                    DwmExtendedFrameBounds,
                    out NativeRectangle rectangle,
                    Marshal.SizeOf<NativeRectangle>()) == 0 ||
                GetWindowRect(handle, out rectangle))
            {
                bounds = rectangle.ToBounds();
                return !bounds.IsEmpty;
            }

            bounds = default;
            return false;
        }

        private static bool TryGetCloaked(IntPtr handle)
        {
            return DwmGetWindowAttribute(
                       handle,
                       DwmCloaked,
                       out int cloaked,
                       sizeof(int)) == 0 &&
                   cloaked != 0;
        }

        private delegate bool EnumWindowsCallback(IntPtr handle, IntPtr parameter);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool EnumWindows(
            EnumWindowsCallback callback,
            IntPtr parameter);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool IsWindowVisible(IntPtr handle);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool IsIconic(IntPtr handle);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetWindowRect(
            IntPtr handle,
            out NativeRectangle rectangle);

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(
            IntPtr handle,
            out int processId);

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern uint GetDpiForWindow(IntPtr handle);

        [DllImport("user32.dll")]
        private static extern IntPtr MonitorFromWindow(IntPtr handle, uint flags);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetMonitorInfo(
            IntPtr monitor,
            ref MonitorInfo monitorInfo);

        [DllImport("dwmapi.dll")]
        private static extern int DwmGetWindowAttribute(
            IntPtr handle,
            uint attribute,
            out NativeRectangle value,
            int valueSize);

        [DllImport("dwmapi.dll")]
        private static extern int DwmGetWindowAttribute(
            IntPtr handle,
            uint attribute,
            out int value,
            int valueSize);

        [StructLayout(LayoutKind.Sequential)]
        private struct NativeRectangle
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;

            public readonly NativeWindowBounds ToBounds()
            {
                return new NativeWindowBounds(Left, Top, Right, Bottom);
            }
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        private struct MonitorInfo
        {
            public int Size;
            public NativeRectangle Monitor;
            public NativeRectangle WorkArea;
            public uint Flags;
        }
    }
}

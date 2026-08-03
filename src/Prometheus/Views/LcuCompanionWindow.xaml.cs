using Prometheus.Desktop.Services;
using Prometheus.ViewModels;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace Prometheus.Views
{
    public partial class LcuCompanionWindow : Window
    {
        private const int ExtendedStyleIndex = -20;
        private const long ToolWindowStyle = 0x00000080L;
        private const long NoActivateStyle = 0x08000000L;
        private const int MouseActivateMessage = 0x0021;
        private const int MouseActivateNoActivate = 3;

        public LcuCompanionWindow(LcuCompanionViewModel viewModel)
        {
            InitializeComponent();
            DataContext = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
            SourceInitialized += HandleSourceInitialized;
        }

        public void ApplyPlacementSide(LcuCompanionSide side)
        {
            var chrome = LcuCompanionChromeCalculator.Calculate(side);
            CompanionChrome.Margin = new Thickness(chrome.Inset);
            CompanionChrome.BorderThickness = new Thickness(
                chrome.LeftBorderThickness,
                chrome.TopBorderThickness,
                chrome.RightBorderThickness,
                chrome.BottomBorderThickness);
            CompanionChrome.CornerRadius = new CornerRadius(
                chrome.TopLeftRadius,
                chrome.TopRightRadius,
                chrome.BottomRightRadius,
                chrome.BottomLeftRadius);
            CompanionShadow.Opacity = chrome.ShowShadow ? 0.24 : 0;
            CompanionSeam.Width = chrome.SeamThickness;
            CompanionSeam.Visibility = chrome.SeamSide == LcuCompanionSeamSide.None
                ? Visibility.Collapsed
                : Visibility.Visible;
            CompanionSeam.HorizontalAlignment =
                chrome.SeamSide == LcuCompanionSeamSide.Right
                    ? HorizontalAlignment.Right
                    : HorizontalAlignment.Left;
        }

        private void HandleSourceInitialized(object sender, EventArgs args)
        {
            var helper = new WindowInteropHelper(this);
            var extendedStyle = GetWindowLongPointer(
                helper.Handle, ExtendedStyleIndex).ToInt64();
            SetWindowLongPointer(
                helper.Handle,
                ExtendedStyleIndex,
                new IntPtr(extendedStyle | ToolWindowStyle | NoActivateStyle));

            if (HwndSource.FromHwnd(helper.Handle) is HwndSource source)
            {
                source.AddHook(HandleWindowMessage);
            }
        }

        private static IntPtr HandleWindowMessage(
            IntPtr window,
            int message,
            IntPtr wParam,
            IntPtr lParam,
            ref bool handled)
        {
            if (message == MouseActivateMessage)
            {
                handled = true;
                return new IntPtr(MouseActivateNoActivate);
            }

            return IntPtr.Zero;
        }

        private static IntPtr GetWindowLongPointer(IntPtr window, int index)
        {
            return IntPtr.Size == 8
                ? GetWindowLongPtr64(window, index)
                : new IntPtr(GetWindowLong32(window, index));
        }

        private static IntPtr SetWindowLongPointer(
            IntPtr window,
            int index,
            IntPtr value)
        {
            return IntPtr.Size == 8
                ? SetWindowLongPtr64(window, index, value)
                : new IntPtr(SetWindowLong32(window, index, value.ToInt32()));
        }

        [DllImport("user32.dll", EntryPoint = "GetWindowLong")]
        private static extern int GetWindowLong32(IntPtr window, int index);

        [DllImport("user32.dll", EntryPoint = "SetWindowLong")]
        private static extern int SetWindowLong32(
            IntPtr window,
            int index,
            int value);

        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtr")]
        private static extern IntPtr GetWindowLongPtr64(IntPtr window, int index);

        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtr")]
        private static extern IntPtr SetWindowLongPtr64(
            IntPtr window,
            int index,
            IntPtr value);
    }
}

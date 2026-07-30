using HandyControl.Controls;
using Prism.Events;
using Prometheus.Core.Events;
using System.ComponentModel;

namespace Prometheus.Views
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private readonly IEventAggregator _eventAggregator;
        private bool _isExitRequested;

        public MainWindow(IEventAggregator eventAggregator)
        {
            InitializeComponent();
            _eventAggregator = eventAggregator;
            Closing += MainWindow_Closing;
            Closed += MainWindow_Closed;
        }

        private void MainWindow_Closing(object sender, CancelEventArgs e)
        {
            if (!_isExitRequested)
            {
                e.Cancel = true;
                Hide();
                return;
            }

            _eventAggregator.GetEvent<WindowClosingEvent>().Publish();
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
            TrayIcon.Dispose();
        }

        private void ShowMainWindow()
        {
            if (!IsVisible)
            {
                Show();
            }

            if (WindowState == System.Windows.WindowState.Minimized)
            {
                WindowState = System.Windows.WindowState.Normal;
            }

            Activate();
        }
    }
}

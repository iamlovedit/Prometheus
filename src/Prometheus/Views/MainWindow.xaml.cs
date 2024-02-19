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
        public MainWindow(IEventAggregator eventAggregator)
        {
            InitializeComponent();
            _eventAggregator = eventAggregator;
            Closing += MainWindow_Closing;
        }

        private void MainWindow_Closing(object sender, CancelEventArgs e)
        {
            _eventAggregator.GetEvent<WindowClosingEvent>().Publish();
        }
    }
}

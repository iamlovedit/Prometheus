using Prometheus.Modules.Setting.ViewModels;
using System.Collections.Specialized;
using System.Windows;
using System.Windows.Threading;

namespace Prometheus.Modules.Setting.Views
{
    /// <summary>
    /// Interaction logic for LogView. Owns the auto-follow behaviour: when the bound
    /// view model's <see cref="LogViewModel.AutoScroll"/> is on, new entries scroll the
    /// list to the bottom. Filter refreshes (Reset) are deliberately left alone.
    /// </summary>
    public partial class LogView : UserControl
    {
        public LogView()
        {
            InitializeComponent();
            Loaded += OnLoaded;
            Unloaded += OnUnloaded;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            if (LogList.Items is INotifyCollectionChanged notifier)
            {
                notifier.CollectionChanged += OnItemsChanged;
            }
        }

        private void OnUnloaded(object sender, RoutedEventArgs e)
        {
            if (LogList.Items is INotifyCollectionChanged notifier)
            {
                notifier.CollectionChanged -= OnItemsChanged;
            }
        }

        private void OnItemsChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            if (e.Action == NotifyCollectionChangedAction.Reset)
            {
                return;
            }

            if (DataContext is not LogViewModel vm || !vm.AutoScroll)
            {
                return;
            }

            if (LogList.Items.Count == 0)
            {
                return;
            }

            LogList.Dispatcher.BeginInvoke(DispatcherPriority.Background, () =>
            {
                if (LogList.Items.Count > 0)
                {
                    LogList.ScrollIntoView(LogList.Items[LogList.Items.Count - 1]);
                }
            });
        }
    }
}

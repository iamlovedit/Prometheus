using System.Collections;
using System.Windows;

namespace Prometheus.Shared.Views
{
    public partial class MatchList : UserControl
    {
        public MatchList()
        {
            InitializeComponent();
        }

        public IEnumerable ItemsSource
        {
            get { return (IEnumerable)GetValue(ItemsSourceProperty); }
            set { SetValue(ItemsSourceProperty, value); }
        }

        public static readonly DependencyProperty ItemsSourceProperty =
            DependencyProperty.Register(nameof(ItemsSource), typeof(IEnumerable), typeof(MatchList), new PropertyMetadata());

        public object SelectedItem
        {
            get { return GetValue(SelectedItemProperty); }
            set { SetValue(SelectedItemProperty, value); }
        }

        public static readonly DependencyProperty SelectedItemProperty =
            DependencyProperty.Register(
                nameof(SelectedItem),
                typeof(object),
                typeof(MatchList),
                new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

        public ICommand ItemCommand
        {
            get { return (ICommand)GetValue(ItemCommandProperty); }
            set { SetValue(ItemCommandProperty, value); }
        }

        public static readonly DependencyProperty ItemCommandProperty =
            DependencyProperty.Register(nameof(ItemCommand), typeof(ICommand), typeof(MatchList), new PropertyMetadata());

        public ICommand SelectionChangedCommand
        {
            get { return (ICommand)GetValue(SelectionChangedCommandProperty); }
            set { SetValue(SelectionChangedCommandProperty, value); }
        }

        public static readonly DependencyProperty SelectionChangedCommandProperty =
            DependencyProperty.Register(nameof(SelectionChangedCommand), typeof(ICommand), typeof(MatchList), new PropertyMetadata());

        private void OnItemPreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (sender is ListBoxItem item)
            {
                ExecuteItemCommand(item.DataContext);
            }
        }

        private void OnItemPreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key is not Key.Enter and not Key.Space)
            {
                return;
            }

            if (sender is ListBoxItem item)
            {
                ExecuteItemCommand(item.DataContext);
                e.Handled = true;
            }
        }

        private void ExecuteItemCommand(object item)
        {
            if (ItemCommand?.CanExecute(item) == true)
            {
                ItemCommand.Execute(item);
            }
        }

        private void OnSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var selectedItem = MatchListBox.SelectedItem;
            if (SelectionChangedCommand?.CanExecute(selectedItem) == true)
            {
                SelectionChangedCommand.Execute(selectedItem);
            }
        }
    }
}

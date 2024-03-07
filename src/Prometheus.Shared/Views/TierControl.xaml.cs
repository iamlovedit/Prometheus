using System.Windows;
using System.Windows.Controls;

namespace Prometheus.Shared.Views
{
    /// <summary>
    /// TierControl.xaml 的交互逻辑
    /// </summary>
    public partial class TierControl : UserControl
    {
        public TierControl()
        {
            InitializeComponent();
        }

        public string TierImage
        {
            get { return (string)GetValue(TierImageProperty); }
            set { SetValue(TierImageProperty, value); }
        }
        public static readonly DependencyProperty TierImageProperty =
            DependencyProperty.Register("TierImage", typeof(string), typeof(TierControl), new PropertyMetadata());




        public string Tier
        {
            get { return (string)GetValue(TierProperty); }
            set { SetValue(TierProperty, value); }
        }

        public static readonly DependencyProperty TierProperty =
            DependencyProperty.Register("Tier", typeof(string), typeof(TierControl), new PropertyMetadata());



        public string TierType
        {
            get { return (string)GetValue(TierTypeProperty); }
            set { SetValue(TierTypeProperty, value); }
        }

        public static readonly DependencyProperty TierTypeProperty =
            DependencyProperty.Register("TierType", typeof(string), typeof(TierControl), new PropertyMetadata());

    }
}

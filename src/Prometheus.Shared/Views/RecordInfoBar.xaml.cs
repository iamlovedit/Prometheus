using System.Windows;
using System.Windows.Controls;

namespace Prometheus.Shared.Views
{
    /// <summary>
    /// Interaction logic for RecordInfoBar.xaml
    /// </summary>
    public partial class RecordInfoBar : UserControl
    {
        public RecordInfoBar()
        {
            InitializeComponent();
        }

        public string KDA
        {
            get { return (string)GetValue(KDAProperty); }
            set { SetValue(KDAProperty, value); }
        }

        public static readonly DependencyProperty KDAProperty =
            DependencyProperty.Register("KDA", typeof(string), typeof(RecordInfoBar), new PropertyMetadata());



        public int Gold
        {
            get { return (int)GetValue(GoldProperty); }
            set { SetValue(GoldProperty, value); }
        }

        public static readonly DependencyProperty GoldProperty =
            DependencyProperty.Register("Gold", typeof(int), typeof(RecordInfoBar), new PropertyMetadata(0));


        public int Damage
        {
            get { return (int)GetValue(DamageProperty); }
            set { SetValue(DamageProperty, value); }
        }

        public static readonly DependencyProperty DamageProperty =
            DependencyProperty.Register("Damage", typeof(int), typeof(RecordInfoBar), new PropertyMetadata(0));


    }
}

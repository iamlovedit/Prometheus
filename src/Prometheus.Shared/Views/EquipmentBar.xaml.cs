using System.Windows;
using System.Windows.Controls;

namespace Prometheus.Shared.Views
{
    /// <summary>
    /// Interaction logic for EquipmentBar.xaml
    /// </summary>
    public partial class EquipmentBar : UserControl
    {
        public EquipmentBar()
        {
            InitializeComponent();
        }

        public string Item0Icon
        {
            get { return (string)GetValue(Item0IconProperty); }
            set { SetValue(Item0IconProperty, value); }
        }

        public static readonly DependencyProperty Item0IconProperty =
            DependencyProperty.Register("Item0Icon", typeof(string), typeof(EquipmentBar), new PropertyMetadata());

        public string Item1Icon
        {
            get { return (string)GetValue(Item1IconProperty); }
            set { SetValue(Item1IconProperty, value); }
        }

        public static readonly DependencyProperty Item1IconProperty =
            DependencyProperty.Register("Item1Icon", typeof(string), typeof(EquipmentBar), new PropertyMetadata());


        public string Item2Icon
        {
            get { return (string)GetValue(Item2IconProperty); }
            set { SetValue(Item2IconProperty, value); }
        }

        public static readonly DependencyProperty Item2IconProperty =
            DependencyProperty.Register("Item2Icon", typeof(string), typeof(EquipmentBar), new PropertyMetadata());


        public string Item3Icon
        {
            get { return (string)GetValue(Item3IconProperty); }
            set { SetValue(Item3IconProperty, value); }
        }

        public static readonly DependencyProperty Item3IconProperty =
            DependencyProperty.Register("Item3Icon", typeof(string), typeof(EquipmentBar), new PropertyMetadata());


        public string Item4Icon
        {
            get { return (string)GetValue(Item4IconProperty); }
            set { SetValue(Item4IconProperty, value); }
        }

        public static readonly DependencyProperty Item4IconProperty =
            DependencyProperty.Register("Item4Icon", typeof(string), typeof(EquipmentBar), new PropertyMetadata());


        public string Item5Icon
        {
            get { return (string)GetValue(Item5IconProperty); }
            set { SetValue(Item5IconProperty, value); }
        }

        public static readonly DependencyProperty Item5IconProperty =
            DependencyProperty.Register("Item5Icon", typeof(string), typeof(EquipmentBar), new PropertyMetadata());


        public string Item6Icon
        {
            get { return (string)GetValue(Item6IconProperty); }
            set { SetValue(Item6IconProperty, value); }
        }

        public static readonly DependencyProperty Item6IconProperty =
            DependencyProperty.Register("Item6Icon", typeof(string), typeof(EquipmentBar), new PropertyMetadata());
    }
}

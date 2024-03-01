using System.Windows;
using System.Windows.Controls;

namespace Prometheus.Shared.Views
{
    /// <summary>
    /// Interaction logic for SpellBar.xaml
    /// </summary>
    public partial class SpellBar : UserControl
    {
        public SpellBar()
        {
            InitializeComponent();
        }


        public string PerkIcon
        {
            get { return (string)GetValue(PerkIconProperty); }
            set { SetValue(PerkIconProperty, value); }
        }

        public static readonly DependencyProperty PerkIconProperty =
            DependencyProperty.Register("PerkIcon", typeof(string), typeof(SpellBar), new PropertyMetadata());



        public string Spell1Icon
        {
            get { return (string)GetValue(Spell1IconProperty); }
            set { SetValue(Spell1IconProperty, value); }
        }

        public static readonly DependencyProperty Spell1IconProperty =
            DependencyProperty.Register("Spell1Icon", typeof(string), typeof(SpellBar), new PropertyMetadata());


        public string Spell2Icon
        {
            get { return (string)GetValue(Spell2IconProperty); }
            set { SetValue(Spell2IconProperty, value); }
        }

        public static readonly DependencyProperty Spell2IconProperty =
            DependencyProperty.Register("Spell2Icon", typeof(string), typeof(SpellBar), new PropertyMetadata());
    }
}

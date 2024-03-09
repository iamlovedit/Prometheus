using Prometheus.Shared.ViewModels;
using System.Windows;
using System.Windows.Controls;

namespace Prometheus.Shared.Views
{

    public partial class DetailControl : UserControl
    {
        public DetailControl()
        {
            InitializeComponent();
        }

        public Team Team
        {
            get { return (Team)GetValue(TeamProperty); }
            set { SetValue(TeamProperty, value); }
        }

        public static readonly DependencyProperty TeamProperty =
            DependencyProperty.Register("Team", typeof(Team), typeof(DetailControl), new PropertyMetadata());
    }
}


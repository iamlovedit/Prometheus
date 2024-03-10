using Prometheus.Shared.Models;
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

    public class EqualHeightStackPanel : StackPanel
    {
        protected override Size MeasureOverride(Size constraint)
        {
            var size = base.MeasureOverride(constraint);
            var itemHeight = size.Height / InternalChildren.Count;

            foreach (UIElement child in InternalChildren)
            {
                child.Measure(new Size(constraint.Width, itemHeight));
            }
            return size;
        }
    }
}


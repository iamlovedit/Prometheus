using Prometheus.Core.Models;
using System.Windows;
using System.Windows.Controls;

namespace Prometheus.Shared.Views
{
    public partial class MasteryControl : UserControl
    {
        public MasteryControl()
        {
            InitializeComponent();
        }

        public int ImageWidth
        {
            get { return (int)GetValue(ImageWidthProperty); }
            set { SetValue(ImageWidthProperty, value); }
        }

        public static readonly DependencyProperty ImageWidthProperty =
            DependencyProperty.Register("ImageWidth", typeof(int), typeof(MasteryControl), new PropertyMetadata(0));


        public int ImageHeight
        {
            get { return (int)GetValue(ImageHeightProperty); }
            set { SetValue(ImageHeightProperty, value); }
        }

        public static readonly DependencyProperty ImageHeightProperty =
            DependencyProperty.Register("ImageHeight", typeof(int), typeof(MasteryControl), new PropertyMetadata(0));

        public ChampionMastery Mastery
        {
            get { return (ChampionMastery)GetValue(MasteryProperty); }
            set { SetValue(MasteryProperty, value); }
        }

        public static readonly DependencyProperty MasteryProperty =
            DependencyProperty.Register("Mastery", typeof(ChampionMastery), typeof(MasteryControl), new PropertyMetadata());

    }
}

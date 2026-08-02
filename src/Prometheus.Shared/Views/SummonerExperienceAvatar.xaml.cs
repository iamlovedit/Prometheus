using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Prometheus.Shared.Views
{
    public partial class SummonerExperienceAvatar : UserControl
    {
        public static readonly DependencyProperty AvatarSizeProperty = DependencyProperty.Register(
            nameof(AvatarSize),
            typeof(double),
            typeof(SummonerExperienceAvatar),
            new PropertyMetadata(112d));

        public static readonly DependencyProperty ProfileIconProperty = DependencyProperty.Register(
            nameof(ProfileIcon),
            typeof(ImageSource),
            typeof(SummonerExperienceAvatar),
            new PropertyMetadata(null));

        public static readonly DependencyProperty SummonerLevelProperty = DependencyProperty.Register(
            nameof(SummonerLevel),
            typeof(object),
            typeof(SummonerExperienceAvatar),
            new PropertyMetadata(null));

        public static readonly DependencyProperty PercentCompleteForNextLevelProperty = DependencyProperty.Register(
            nameof(PercentCompleteForNextLevel),
            typeof(double),
            typeof(SummonerExperienceAvatar),
            new PropertyMetadata(0d));

        public static readonly DependencyProperty XpSinceLastLevelProperty = DependencyProperty.Register(
            nameof(XpSinceLastLevel),
            typeof(int),
            typeof(SummonerExperienceAvatar),
            new PropertyMetadata(0));

        public static readonly DependencyProperty XpUntilNextLevelProperty = DependencyProperty.Register(
            nameof(XpUntilNextLevel),
            typeof(int),
            typeof(SummonerExperienceAvatar),
            new PropertyMetadata(0));

        public static readonly DependencyProperty ShowExperienceToolTipProperty = DependencyProperty.Register(
            nameof(ShowExperienceToolTip),
            typeof(bool),
            typeof(SummonerExperienceAvatar),
            new PropertyMetadata(true));

        public SummonerExperienceAvatar()
        {
            InitializeComponent();
        }

        public double AvatarSize
        {
            get => (double)GetValue(AvatarSizeProperty);
            set => SetValue(AvatarSizeProperty, value);
        }

        public ImageSource ProfileIcon
        {
            get => (ImageSource)GetValue(ProfileIconProperty);
            set => SetValue(ProfileIconProperty, value);
        }

        public object SummonerLevel
        {
            get => GetValue(SummonerLevelProperty);
            set => SetValue(SummonerLevelProperty, value);
        }

        public double PercentCompleteForNextLevel
        {
            get => (double)GetValue(PercentCompleteForNextLevelProperty);
            set => SetValue(PercentCompleteForNextLevelProperty, value);
        }

        public int XpSinceLastLevel
        {
            get => (int)GetValue(XpSinceLastLevelProperty);
            set => SetValue(XpSinceLastLevelProperty, value);
        }

        public int XpUntilNextLevel
        {
            get => (int)GetValue(XpUntilNextLevelProperty);
            set => SetValue(XpUntilNextLevelProperty, value);
        }

        public bool ShowExperienceToolTip
        {
            get => (bool)GetValue(ShowExperienceToolTipProperty);
            set => SetValue(ShowExperienceToolTipProperty, value);
        }
    }
}

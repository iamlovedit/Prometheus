namespace Prometheus.Desktop.Services
{
    public enum LcuCompanionSeamSide
    {
        None,
        Left,
        Right
    }

    public readonly record struct LcuCompanionChrome(
        double LeftBorderThickness,
        double TopBorderThickness,
        double RightBorderThickness,
        double BottomBorderThickness,
        double TopLeftRadius,
        double TopRightRadius,
        double BottomRightRadius,
        double BottomLeftRadius,
        double Inset,
        bool ShowShadow,
        LcuCompanionSeamSide SeamSide,
        double SeamThickness);

    public static class LcuCompanionChromeCalculator
    {
        private const double SeamThickness = 3;
        private const double BorderThickness = 1;
        private const double OuterRadius = 14;
        private const double OverlayInset = 8;

        public static LcuCompanionChrome Calculate(LcuCompanionSide side)
        {
            return side switch
            {
                LcuCompanionSide.Right => new LcuCompanionChrome(
                    BorderThickness,
                    BorderThickness,
                    BorderThickness,
                    BorderThickness,
                    0,
                    OuterRadius,
                    OuterRadius,
                    0,
                    0,
                    false,
                    LcuCompanionSeamSide.Left,
                    SeamThickness),
                LcuCompanionSide.Left => new LcuCompanionChrome(
                    BorderThickness,
                    BorderThickness,
                    BorderThickness,
                    BorderThickness,
                    OuterRadius,
                    0,
                    0,
                    OuterRadius,
                    0,
                    false,
                    LcuCompanionSeamSide.Right,
                    SeamThickness),
                _ => new LcuCompanionChrome(
                    BorderThickness,
                    BorderThickness,
                    BorderThickness,
                    BorderThickness,
                    OuterRadius,
                    OuterRadius,
                    OuterRadius,
                    OuterRadius,
                    OverlayInset,
                    true,
                    LcuCompanionSeamSide.None,
                    0)
            };
        }
    }
}

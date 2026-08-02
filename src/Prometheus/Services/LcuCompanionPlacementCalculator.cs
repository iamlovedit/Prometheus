namespace Prometheus.Desktop.Services
{
    public enum LcuCompanionSide
    {
        Right,
        Left,
        InsideRight
    }

    public readonly record struct LcuCompanionPlacement(
        int Left,
        int Top,
        int Width,
        int Height,
        LcuCompanionSide Side);

    public static class LcuCompanionPlacementCalculator
    {
        public const double DefaultWidth = 344;

        public static LcuCompanionPlacement Calculate(
            LcuWindowState state,
            double desiredWidth = DefaultWidth)
        {
            if (state?.IsAvailable != true)
            {
                throw new ArgumentException(
                    "An available LCU window is required.", nameof(state));
            }

            var dpi = state.Dpi > 0 ? state.Dpi : 96;
            var width = Math.Max(1,
                (int)Math.Round(desiredWidth * dpi / 96d,
                    MidpointRounding.AwayFromZero));
            var workArea = state.WorkArea.IsEmpty ? state.Bounds : state.WorkArea;
            width = Math.Min(width, workArea.Width);
            var height = Math.Min(state.Bounds.Height, workArea.Height);
            var top = Math.Clamp(state.Bounds.Top,
                workArea.Top,
                Math.Max(workArea.Top, workArea.Bottom - height));

            if (workArea.Right - state.Bounds.Right >= width)
            {
                return new LcuCompanionPlacement(
                    state.Bounds.Right,
                    top,
                    width,
                    height,
                    LcuCompanionSide.Right);
            }

            if (state.Bounds.Left - workArea.Left >= width)
            {
                return new LcuCompanionPlacement(
                    state.Bounds.Left - width,
                    top,
                    width,
                    height,
                    LcuCompanionSide.Left);
            }

            return new LcuCompanionPlacement(
                Math.Clamp(state.Bounds.Right - width,
                    workArea.Left,
                    Math.Max(workArea.Left, workArea.Right - width)),
                top,
                width,
                height,
                LcuCompanionSide.InsideRight);
        }
    }
}

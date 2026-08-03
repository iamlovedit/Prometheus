namespace Prometheus.Desktop.Services
{
    public readonly record struct LcuCompanionZOrder(
        IntPtr InsertAfter,
        bool PreserveCurrent);

    public static class LcuCompanionZOrderCalculator
    {
        public static LcuCompanionZOrder Calculate(
            IntPtr lcuHandle,
            IntPtr companionHandle,
            Func<IntPtr, IntPtr> getPreviousWindow)
        {
            if (lcuHandle == IntPtr.Zero)
            {
                throw new ArgumentException(
                    "An LCU window handle is required.", nameof(lcuHandle));
            }

            if (companionHandle == IntPtr.Zero)
            {
                throw new ArgumentException(
                    "A companion window handle is required.",
                    nameof(companionHandle));
            }

            ArgumentNullException.ThrowIfNull(getPreviousWindow);

            var previousWindow = getPreviousWindow(lcuHandle);
            return previousWindow == companionHandle
                ? new LcuCompanionZOrder(IntPtr.Zero, true)
                : new LcuCompanionZOrder(previousWindow, false);
        }
    }
}

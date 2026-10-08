namespace Prometheus.Desktop.Services
{
    public readonly record struct LcuCompanionZOrder(
        IntPtr InsertAfter,
        bool PreserveCurrent);

    public static class LcuCompanionZOrderCalculator
    {
        private static readonly IntPtr NoTopmost = new(-2);

        public static LcuCompanionZOrder Calculate(
            IntPtr lcuHandle,
            IntPtr companionHandle,
            Func<IntPtr, IntPtr> getPreviousWindow,
            Func<IntPtr, bool> isTopmost = null)
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
            isTopmost ??= _ => false;

            var previousWindow = getPreviousWindow(lcuHandle);
            if (previousWindow == companionHandle)
            {
                return isTopmost(companionHandle)
                    ? new LcuCompanionZOrder(NoTopmost, false)
                    : new LcuCompanionZOrder(IntPtr.Zero, true);
            }

            if (previousWindow != IntPtr.Zero && isTopmost(previousWindow))
            {
                // HWND_NOTOPMOST does not raise an already ordinary window.
                return new LcuCompanionZOrder(
                    isTopmost(companionHandle) ? NoTopmost : IntPtr.Zero,
                    false);
            }

            return new LcuCompanionZOrder(previousWindow, false);
        }
    }
}

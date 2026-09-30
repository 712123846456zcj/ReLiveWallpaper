using System;
using System.Threading;

namespace Lively.Player.Overlay.Interop
{
    /// <summary>
    /// Sleep with a high resolution waitable timer.
    ///
    /// Thread.Sleep is bound to the system timer granularity (15.6ms by default) which makes
    /// frame pacing impossible, a winforms timer additionally adds the message loop latency.
    /// CreateWaitableTimerEx with the high resolution flag gives sub millisecond wake ups
    /// without raising the timer resolution of the whole system like timeBeginPeriod would.
    /// </summary>
    internal sealed class PrecisionWaiter : IDisposable
    {
        private const uint CREATE_WAITABLE_TIMER_HIGH_RESOLUTION = 0x00000002;
        private const uint TIMER_ALL_ACCESS = 0x001F0003;

        private readonly IntPtr timer;
        private bool disposed;

        public PrecisionWaiter()
        {
            try
            {
                // Zero when high resolution timers are unavailable, Wait() falls back then.
                timer = NativeMethods.CreateWaitableTimerExW(IntPtr.Zero, null,
                    CREATE_WAITABLE_TIMER_HIGH_RESOLUTION, TIMER_ALL_ACCESS);
            }
            catch
            {
                timer = IntPtr.Zero;
            }
        }

        public bool IsHighResolution => timer != IntPtr.Zero;

        /// <summary>Waits for the given amount of seconds.</summary>
        public void Wait(double seconds)
        {
            if (disposed || seconds <= 0.0)
                return;

            if (timer == IntPtr.Zero)
            {
                Thread.Sleep(FallbackMilliseconds(seconds));
                return;
            }

            // Negative due time is relative, the unit is 100ns.
            var due = -(long)Math.Round(seconds * 10000000.0);
            if (due > -1L)
                due = -1L;

            if (!NativeMethods.SetWaitableTimer(timer, ref due, 0, IntPtr.Zero, IntPtr.Zero, false))
            {
                Thread.Sleep(FallbackMilliseconds(seconds));
                return;
            }

            NativeMethods.WaitForSingleObject(timer, NativeMethods.INFINITE);
        }

        private static int FallbackMilliseconds(double seconds) => Math.Max(1, (int)Math.Round(seconds * 1000.0));

        public void Dispose()
        {
            if (disposed)
                return;

            disposed = true;
            if (timer != IntPtr.Zero)
                NativeMethods.CloseHandle(timer);
        }
    }
}

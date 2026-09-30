using System;

namespace Lively.Player.Overlay.Interop
{
    /// <summary>
    /// Keeps the overlay window just above the desktop window.
    ///
    /// The wallpaper (and the desktop icons) live inside "Progman", therefore a top
    /// level window placed directly above Progman is drawn over the wallpaper while
    /// still staying behind every normal application window. When the shell raises
    /// the desktop to the front (show desktop / peek) the overlay is hidden so it
    /// can never end up covering an application window.
    /// </summary>
    internal sealed class DesktopZOrder
    {
        private readonly IntPtr hwnd;
        private readonly int x;
        private readonly int y;
        private readonly int width;
        private readonly int height;
        private IntPtr progman;
        private bool visible;

        public DesktopZOrder(IntPtr hwnd, int x, int y, int width, int height)
        {
            this.hwnd = hwnd;
            this.x = x;
            this.y = y;
            this.width = width;
            this.height = height;
        }

        /// <summary>Returns true when the z-order had to be changed.</summary>
        public bool Refresh()
        {
            if (progman == IntPtr.Zero || !NativeMethods.IsWindow(progman))
            {
                progman = NativeMethods.GetProgramManager();
                if (progman == IntPtr.Zero)
                    return false;
            }

            // "Show desktop" / peek raises Progman above the other windows, the shell
            // then reports a window below the desktop again.
            var desktopAtBottom = NativeMethods.GetWindow(progman, NativeMethods.GW_HWNDNEXT) == IntPtr.Zero;
            if (!desktopAtBottom)
            {
                if (visible)
                {
                    NativeMethods.ShowWindow(hwnd, NativeMethods.SW_HIDE);
                    visible = false;
                    return true;
                }
                return false;
            }

            if (!visible)
            {
                NativeMethods.ShowWindow(hwnd, NativeMethods.SW_SHOWNOACTIVATE);
                visible = true;
            }

            var above = NativeMethods.GetWindow(progman, NativeMethods.GW_HWNDPREV);
            if (above == hwnd)
                return false;

            if (above == IntPtr.Zero)
            {
                // Progman is the only window left, nothing sensible to insert after.
                return false;
            }

            NativeMethods.SetWindowPos(hwnd, above, x, y, width, height,
                NativeMethods.SWP_NOACTIVATE);
            return true;
        }
    }
}

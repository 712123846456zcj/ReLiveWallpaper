using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace Lively.Player.Overlay.Interop
{
    /// <summary>
    /// Off screen ARGB surface presented through UpdateLayeredWindow.
    ///
    /// The effect is drawn with GDI+ into a 32bpp bitmap which GDI+ already stores as
    /// premultiplied alpha, so the pixels are copied into the persistent DIB section
    /// verbatim and then blitted to the layered window. The staging buffer is reused
    /// so that no large allocation happens per frame.
    /// </summary>
    internal sealed class LayeredSurface : IDisposable
    {
        private readonly int width;
        private readonly int height;
        private readonly int rowBytes;
        private readonly Bitmap bitmap;
        private readonly Graphics graphics;
        private readonly byte[] staging;

        private readonly IntPtr screenDc;
        private readonly IntPtr memoryDc;
        private readonly IntPtr dib;
        private readonly IntPtr previousDib;
        private readonly IntPtr bits;

        private bool disposed;

        public LayeredSurface(int width, int height)
        {
            this.width = width;
            this.height = height;
            rowBytes = width * 4;

            bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);
            graphics = Graphics.FromImage(bitmap);
            graphics.CompositingMode = CompositingMode.SourceOver;
            graphics.CompositingQuality = CompositingQuality.HighSpeed;
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.InterpolationMode = InterpolationMode.Low;
            graphics.PixelOffsetMode = PixelOffsetMode.Half;
            graphics.Clear(Color.Transparent);

            staging = new byte[rowBytes * height];

            screenDc = NativeMethods.GetDC(IntPtr.Zero);
            memoryDc = NativeMethods.CreateCompatibleDC(screenDc);

            var info = new NativeMethods.BITMAPINFO
            {
                bmiHeader = new NativeMethods.BITMAPINFOHEADER
                {
                    biSize = Marshal.SizeOf<NativeMethods.BITMAPINFOHEADER>(),
                    biWidth = width,
                    biHeight = -height, // top down
                    biPlanes = 1,
                    biBitCount = 32,
                    biCompression = NativeMethods.BI_RGB
                },
                bmiColors = new uint[256]
            };

            dib = NativeMethods.CreateDIBSection(memoryDc, ref info, NativeMethods.DIB_RGB_COLORS, out bits, IntPtr.Zero, 0);
            if (dib == IntPtr.Zero || bits == IntPtr.Zero)
                throw new InvalidOperationException("Failed to create the overlay DIB section.");

            previousDib = NativeMethods.SelectObject(memoryDc, dib);
        }

        public Graphics Graphics => graphics;

        public Bitmap Bitmap => bitmap;

        public int Width => width;

        public int Height => height;

        /// <summary>Copy the rendered frame into the DIB and blit it to the window.</summary>
        public bool Present(IntPtr hwnd, int x, int y)
        {
            CopyToDib();
            return Blit(hwnd, x, y);
        }

        public void Save(string path)
        {
            bitmap.Save(path, ImageFormat.Png);
        }

        private void CopyToDib()
        {
            var data = bitmap.LockBits(new Rectangle(0, 0, width, height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            try
            {
                var stride = data.Stride;
                if (stride == rowBytes)
                {
                    Marshal.Copy(data.Scan0, staging, 0, staging.Length);
                }
                else
                {
                    for (int y = 0; y < height; y++)
                        Marshal.Copy(IntPtr.Add(data.Scan0, y * stride), staging, y * rowBytes, rowBytes);
                }
            }
            finally
            {
                bitmap.UnlockBits(data);
            }

            // The bits of a GdiPlus 32bppArgb bitmap are already premultiplied,
            // premultiplying again would darken every semi transparent pixel.
            Marshal.Copy(staging, 0, bits, staging.Length);
        }

        private bool Blit(IntPtr hwnd, int x, int y)
        {
            var destination = new NativeMethods.POINT { X = x, Y = y };
            var source = new NativeMethods.POINT { X = 0, Y = 0 };
            var size = new NativeMethods.SIZE { cx = width, cy = height };
            var blend = new NativeMethods.BLENDFUNCTION
            {
                BlendOp = NativeMethods.AC_SRC_OVER,
                BlendFlags = 0,
                SourceConstantAlpha = 255,
                AlphaFormat = NativeMethods.AC_SRC_ALPHA
            };

            return NativeMethods.UpdateLayeredWindow(hwnd, screenDc, ref destination, ref size,
                memoryDc, ref source, 0, ref blend, NativeMethods.ULW_ALPHA);
        }

        public void Dispose()
        {
            if (disposed)
                return;
            disposed = true;

            graphics.Dispose();
            bitmap.Dispose();

            if (previousDib != IntPtr.Zero)
                NativeMethods.SelectObject(memoryDc, previousDib);
            if (dib != IntPtr.Zero)
                NativeMethods.DeleteObject(dib);
            if (memoryDc != IntPtr.Zero)
                NativeMethods.DeleteDC(memoryDc);
            if (screenDc != IntPtr.Zero)
                NativeMethods.ReleaseDC(IntPtr.Zero, screenDc);
        }
    }
}

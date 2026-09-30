using System;
using System.Drawing;
using System.Globalization;
using System.Windows.Forms;
using CommandLine;
using Lively.Player.Overlay.Effects;
using Lively.Player.Overlay.Interop;

namespace Lively.Player.Overlay
{
    internal static class Program
    {
        [STAThread]
        private static void Main(string[] args)
        {
            var startArgs = new StartArgs();
            Parser.Default.ParseArguments<StartArgs>(args).WithParsed(x => startArgs = x);

            try
            {
                NativeMethods.SetProcessDpiAwarenessContext(NativeMethods.DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2);
            }
            catch
            {
                // Already set by the application manifest.
            }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            SpriteLibrary.Initialize();

            var bounds = ParseBounds(startArgs.Bounds);
            if (bounds.Width <= 0 || bounds.Height <= 0)
            {
                Log.Error("Invalid bounds: " + startArgs.Bounds);
                return;
            }

            if (!string.IsNullOrWhiteSpace(startArgs.SnapshotPath))
            {
                Snapshot(startArgs, bounds);
                return;
            }

            Application.Run(new OverlayForm(startArgs, bounds));
        }

        private static Rectangle ParseBounds(string value)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                var parts = value.Split(',');
                if (parts.Length == 4 &&
                    int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var x) &&
                    int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var y) &&
                    int.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out var width) &&
                    int.TryParse(parts[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out var height))
                {
                    return new Rectangle(x, y, width, height);
                }
            }

            return Screen.PrimaryScreen?.Bounds ?? Rectangle.Empty;
        }

        /// <summary>Debug helper: render a frame to a png without a window.</summary>
        private static void Snapshot(StartArgs startArgs, Rectangle bounds)
        {
            var effect = EffectCatalog.Create(startArgs.Effect);
            if (effect == null)
            {
                Log.Error("Unknown effect: " + startArgs.Effect);
                return;
            }

            var properties = EffectProperties.Load(startArgs.PropertyPath);
            using (var surface = new LayeredSurface(bounds.Width, bounds.Height))
            {
                var context = new EffectContext();
                var total = Math.Max(0.1, startArgs.SnapshotTime);
                const double step = 1.0 / 60.0;
                for (double t = 0; t < total; t += step)
                {
                    surface.Graphics.Clear(Color.Transparent);
                    context.Graphics = surface.Graphics;
                    context.Width = bounds.Width;
                    context.Height = bounds.Height;
                    context.DeltaSeconds = step;
                    context.ElapsedSeconds = t;
                    context.Properties = properties;
                    effect.Render(context);
                }

                surface.Save(startArgs.SnapshotPath);
                Log.Info("Snapshot saved: " + startArgs.SnapshotPath);
            }
        }
    }
}

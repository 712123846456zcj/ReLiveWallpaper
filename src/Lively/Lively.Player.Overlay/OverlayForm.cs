using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Windows.Forms;
using Lively.Player.Overlay.Effects;
using Lively.Player.Overlay.Interop;

namespace Lively.Player.Overlay
{
    /// <summary>
    /// Click through, per pixel transparent window that renders an effect on top of
    /// the desktop wallpaper.
    /// </summary>
    internal sealed class OverlayForm : Form
    {
        private const int MessageTypeClose = 5;   // cmd_close
        private const int MessageTypeReload = 4;  // cmd_reload
        private const int MessageTypeSuspend = 7; // cmd_suspend
        private const int MessageTypeResume = 8;  // cmd_resume

        /// <summary>Longest single sleep of the render loop, bounds the reaction time to suspend/close.</summary>
        private const double MaxWaitSeconds = 0.05;
        /// <summary>How often the measured frame rate is written to the log.</summary>
        private const double FpsLogIntervalSeconds = 300.0;

        private readonly StartArgs startArgs;
        private readonly Rectangle bounds;
        private readonly LayeredSurface surface;
        private readonly DesktopZOrder zOrder;
        private readonly System.Windows.Forms.Timer zOrderTimer;
        private readonly PrecisionWaiter waiter = new PrecisionWaiter();
        private readonly Stopwatch clock = Stopwatch.StartNew();
        private readonly EffectContext context = new EffectContext();
        private readonly IntPtr hwnd;

        private IEffect effect;
        private volatile EffectProperties properties;
        private Thread renderThread;
        private double lastFrameTime;
        private long frameCount;
        private double fpsWindowStart;
        private volatile bool closing;
        private volatile bool suspended;
        /// <summary>True when the core started this process and owns the other end of stdin.</summary>
        private readonly bool hasParentPipe = NativeMethods.HasParentPipe();

        public OverlayForm(StartArgs startArgs, Rectangle bounds)
        {
            this.startArgs = startArgs;
            this.bounds = bounds;

            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            MinimizeBox = false;
            MaximizeBox = false;
            Text = "Lively Overlay";
            BackColor = Color.Black;
            SetStyle(ControlStyles.Opaque | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint, true);
            DoubleBuffered = false;

            Bounds = bounds;

            // Handle is needed before the extended styles can be changed. It is cached so
            // that the render thread never has to touch the control from a foreign thread.
            hwnd = Handle;
            var exStyle = NativeMethods.GetWindowLong(hwnd, NativeMethods.GWL_EXSTYLE);
            NativeMethods.SetWindowLong(hwnd, NativeMethods.GWL_EXSTYLE,
                exStyle | NativeMethods.WS_EX_LAYERED | NativeMethods.WS_EX_TRANSPARENT |
                NativeMethods.WS_EX_NOACTIVATE | NativeMethods.WS_EX_TOOLWINDOW);

            surface = new LayeredSurface(bounds.Width, bounds.Height);
            zOrder = new DesktopZOrder(hwnd, bounds.X, bounds.Y, bounds.Width, bounds.Height);

            effect = EffectCatalog.Create(startArgs.Effect);
            properties = EffectProperties.Load(startArgs.PropertyPath);

            zOrderTimer = new System.Windows.Forms.Timer { Interval = 500 };
            zOrderTimer.Tick += (s, e) => zOrder.Refresh();
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);

            if (effect == null)
            {
                Log.Error("Unknown effect: " + startArgs.Effect);
                Close();
                return;
            }

            zOrder.Refresh();
            lastFrameTime = clock.Elapsed.TotalSeconds;
            fpsWindowStart = lastFrameTime;

            renderThread = new Thread(RenderLoop)
            {
                IsBackground = true,
                Name = "overlay-render"
            };
            renderThread.Start();
            zOrderTimer.Start();

            PublishHwnd();
            if (hasParentPipe)
                StartIpcReader();

            Log.Info("Overlay started: effect=" + startArgs.Effect + " bounds=" + bounds + " display=" + startArgs.Display);
        }

        private void PublishHwnd()
        {
            try
            {
                // Lively core ipc: {"Type":0,"Hwnd":N}
                Console.Out.WriteLine("{\"Type\":0,\"Hwnd\":" + hwnd.ToInt64() + "}");
                Console.Out.Flush();
            }
            catch (Exception e)
            {
                Log.Error(e);
            }
        }

        private void StartIpcReader()
        {
            var thread = new Thread(IpcLoop)
            {
                IsBackground = true,
                Name = "overlay-ipc"
            };
            thread.Start();
        }

        private void IpcLoop()
        {
            try
            {
                string line;
                while ((line = Console.In.ReadLine()) != null)
                {
                    var type = ReadMessageType(line);
                    switch (type)
                    {
                        case MessageTypeReload:
                            BeginInvokeSafe(ReloadProperties);
                            break;
                        case MessageTypeSuspend:
                            BeginInvokeSafe(Suspend);
                            break;
                        case MessageTypeResume:
                            BeginInvokeSafe(Resume);
                            break;
                        case MessageTypeClose:
                            BeginInvokeSafe(Close);
                            return;
                    }
                }
            }
            catch (Exception e)
            {
                Log.Error(e);
            }

            // The pipe is closed, the parent process is gone.
            BeginInvokeSafe(Close);
        }

        private void BeginInvokeSafe(Action action)
        {
            try
            {
                if (IsHandleCreated && !IsDisposed)
                    BeginInvoke(action);
            }
            catch (Exception e)
            {
                Log.Error(e);
            }
        }

        /// <summary>Reads the "Type" field of an ipc json message.</summary>
        private static int ReadMessageType(string json)
        {
            try
            {
                using (var document = JsonDocument.Parse(json))
                {
                    if (document.RootElement.TryGetProperty("Type", out var type) && type.TryGetInt32(out var value))
                        return value;
                }
            }
            catch
            {
            }
            return -1;
        }

        /// <summary>Stops rendering, the wallpaper is not visible (fullscreen app, paused..).</summary>
        private void Suspend()
        {
            if (closing || suspended)
                return;

            // Only the rendering stops, the window is left alone so that a quickly
            // following resume does not make it flicker.
            suspended = true;
            Log.Info("Overlay suspended: effect=" + startArgs.Effect);
        }

        private void Resume()
        {
            if (closing || !suspended)
                return;

            suspended = false;
            zOrder.Refresh();
            Log.Info("Overlay resumed: effect=" + startArgs.Effect);
        }

        private void ReloadProperties()
        {
            try
            {
                properties = EffectProperties.Load(startArgs.PropertyPath);
            }
            catch (Exception e)
            {
                Log.Error(e);
            }
        }

        /// <summary>
        /// Render thread of the overlay.
        ///
        /// The loop paces itself against the wall clock instead of using a timer, a timer
        /// would add its own interval and the message loop latency on top of every frame
        /// (a 33ms timer clocked in at ~21 fps). Sleeping happens in short slices so that
        /// suspend and close still react right away.
        /// </summary>
        private void RenderLoop()
        {
            var nextFrame = clock.Elapsed.TotalSeconds;
            while (!closing)
            {
                if (suspended)
                {
                    waiter.Wait(MaxWaitSeconds);
                    nextFrame = clock.Elapsed.TotalSeconds;
                    continue;
                }

                var interval = Math.Max(1, Math.Min(1000, effect.FrameIntervalMs)) / 1000.0;
                var now = clock.Elapsed.TotalSeconds;
                var wait = nextFrame - now;
                if (wait > 0.0)
                {
                    waiter.Wait(Math.Min(wait, MaxWaitSeconds));
                    continue;
                }

                // Frames that could not be drawn in time are dropped, the effect
                // advances by the real elapsed time so the animation never speeds up.
                nextFrame = Math.Max(nextFrame + interval, now);
                RenderFrame(now);
            }
        }

        private void RenderFrame(double now)
        {
            try
            {
                var delta = now - lastFrameTime;
                lastFrameTime = now;

                surface.Graphics.Clear(Color.Transparent);

                context.Graphics = surface.Graphics;
                context.Width = bounds.Width;
                context.Height = bounds.Height;
                context.DeltaSeconds = delta;
                context.ElapsedSeconds = now;
                context.Properties = properties;
                effect.Render(context);

                if (!surface.Present(hwnd, bounds.X, bounds.Y) && frameCount % 120 == 0)
                    Log.Error("UpdateLayeredWindow failed.");

                frameCount++;
                if (now - fpsWindowStart >= FpsLogIntervalSeconds)
                {
                    Log.Info("fps=" + Math.Round(frameCount / (now - fpsWindowStart), 1) + " effect=" + startArgs.Effect);
                    frameCount = 0;
                    fpsWindowStart = now;
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex);
            }
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            closing = true;
            zOrderTimer.Stop();
            StopRenderThread();
            base.OnFormClosing(e);
        }

        /// <summary>Waits for the render loop to leave, the surface may only be disposed afterwards.</summary>
        private void StopRenderThread()
        {
            var thread = renderThread;
            renderThread = null;
            if (thread is null || !thread.IsAlive)
                return;

            if (!thread.Join((int)(MaxWaitSeconds * 1000 * 20)))
                Log.Error("Render thread did not stop in time.");
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                zOrderTimer?.Dispose();
                effect?.Dispose();
                surface?.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}

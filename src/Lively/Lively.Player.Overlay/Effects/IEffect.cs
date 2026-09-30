using System;
using System.Drawing;

namespace Lively.Player.Overlay.Effects
{
    /// <summary>Per frame state handed to an effect.</summary>
    internal sealed class EffectContext
    {
        public Graphics Graphics { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }
        /// <summary>Seconds since the previous frame, already clamped.</summary>
        public double DeltaSeconds { get; set; }
        /// <summary>Seconds since the effect started.</summary>
        public double ElapsedSeconds { get; set; }
        public EffectProperties Properties { get; set; }
    }

    internal interface IEffect : IDisposable
    {
        string Id { get; }
        /// <summary>Simulate and draw one frame, the surface is cleared before the call.</summary>
        void Render(EffectContext context);
        /// <summary>Requested frame interval in milliseconds.</summary>
        int FrameIntervalMs { get; }
    }
}

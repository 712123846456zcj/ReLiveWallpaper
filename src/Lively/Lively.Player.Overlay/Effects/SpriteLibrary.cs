using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace Lively.Player.Overlay.Effects
{
    /// <summary>
    /// Pre-rendered alpha sprites shared by the effects.
    /// Particles are drawn by blitting these bitmaps, which keeps the per frame
    /// cost limited to small DrawImageUnscaled calls instead of building gradients.
    /// Each sprite is baked at several opacity levels so that per particle alpha
    /// can be selected without allocating ImageAttributes.
    /// </summary>
    internal static class SpriteLibrary
    {
        public const int AlphaLevels = 6;
        private static readonly float[] alphaScale = { 0.15f, 0.30f, 0.45f, 0.60f, 0.80f, 1.00f };

        /// <summary>Rain streak angles, degrees. Index 2 is vertical.</summary>
        public static readonly float[] StreakAngles = { -24f, -12f, 0f, 12f, 24f };
        public const int VerticalAngleIndex = 2;

        public static readonly int[] StreakLengths = { 16, 26, 40, 60, 88, 130 };
        public static readonly int[] DropRadii = { 2, 4, 6, 9, 13 };
        public static readonly int[] RippleDiameters = { 12, 20, 30, 44, 60, 80 };
        public static readonly int[] TrailLengths = { 16, 28, 44, 64 };

        // [angle][length][alpha]
        private static Bitmap[][][] streaks;
        // [radius][alpha]
        private static Bitmap[][] drops;
        // [size][alpha]
        private static Bitmap[][] ripples;
        // [length][alpha]
        private static Bitmap[][] trails;
        // [size][alpha]
        private static Bitmap[][] sparkles;

        private static bool initialized;

        public static Bitmap GetStreak(int angleIndex, int lengthIndex, float alpha)
            => streaks[angleIndex][lengthIndex][AlphaIndex(alpha)];

        public static Bitmap GetDrop(int radiusIndex, float alpha)
            => drops[radiusIndex][AlphaIndex(alpha)];

        public static Bitmap GetRipple(int sizeIndex, float alpha)
            => ripples[sizeIndex][AlphaIndex(alpha)];

        public static Bitmap GetTrail(int lengthIndex, float alpha)
            => trails[lengthIndex][AlphaIndex(alpha)];

        public static Bitmap GetSparkle(int sizeIndex, float alpha)
            => sparkles[sizeIndex][AlphaIndex(alpha)];

        public static int AlphaIndex(float alpha)
        {
            int index = Array.BinarySearch(alphaScale, alpha);
            if (index >= 0)
                return index;
            index = ~index;
            if (index <= 0)
                return 0;
            if (index >= alphaScale.Length)
                return alphaScale.Length - 1;
            // nearest neighbour
            return (alpha - alphaScale[index - 1]) < (alphaScale[index] - alpha) ? index - 1 : index;
        }

        public static void Initialize()
        {
            if (initialized)
                return;
            initialized = true;

            streaks = new Bitmap[StreakAngles.Length][][];
            for (int a = 0; a < StreakAngles.Length; a++)
            {
                streaks[a] = new Bitmap[StreakLengths.Length][];
                for (int l = 0; l < StreakLengths.Length; l++)
                {
                    streaks[a][l] = new Bitmap[AlphaLevels];
                    for (int i = 0; i < AlphaLevels; i++)
                        streaks[a][l][i] = CreateStreak(StreakAngles[a], StreakLengths[l], alphaScale[i]);
                }
            }

            drops = new Bitmap[DropRadii.Length][];
            for (int r = 0; r < DropRadii.Length; r++)
            {
                drops[r] = new Bitmap[AlphaLevels];
                for (int i = 0; i < AlphaLevels; i++)
                    drops[r][i] = CreateDrop(DropRadii[r], alphaScale[i]);
            }

            ripples = new Bitmap[RippleDiameters.Length][];
            for (int s = 0; s < RippleDiameters.Length; s++)
            {
                ripples[s] = new Bitmap[AlphaLevels];
                for (int i = 0; i < AlphaLevels; i++)
                    ripples[s][i] = CreateRipple(RippleDiameters[s], alphaScale[i]);
            }

            trails = new Bitmap[TrailLengths.Length][];
            for (int l = 0; l < TrailLengths.Length; l++)
            {
                trails[l] = new Bitmap[AlphaLevels];
                for (int i = 0; i < AlphaLevels; i++)
                    trails[l][i] = CreateTrail(TrailLengths[l], alphaScale[i]);
            }

            sparkles = new Bitmap[3][];
            for (int s = 0; s < 3; s++)
            {
                sparkles[s] = new Bitmap[AlphaLevels];
                for (int i = 0; i < AlphaLevels; i++)
                    sparkles[s][i] = CreateSparkle(3 + s * 3, alphaScale[i]);
            }
        }

        private static Bitmap CreateCanvas(int width, int height)
        {
            var bmp = new Bitmap(Math.Max(2, width), Math.Max(2, height), PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(bmp))
            {
                g.Clear(Color.Transparent);
            }
            return bmp;
        }

        /// <summary>
        /// Vertical soft streak: transparent at both ends, bright in the upper third,
        /// with a solid core and softer edges.
        /// </summary>
        private static Bitmap CreateStreak(float angleDegrees, int length, float alpha)
        {
            double rad = angleDegrees * Math.PI / 180.0;
            int pad = 4;
            int softWidth = 5;
            int width = (int)Math.Ceiling(softWidth + Math.Abs(Math.Sin(rad)) * length) + pad * 2;
            int height = (int)Math.Ceiling(Math.Cos(rad) * length) + pad * 2;
            var bmp = CreateCanvas(width, height);

            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.InterpolationMode = InterpolationMode.HighQualityBilinear;
                g.TranslateTransform(width / 2f, pad);
                g.RotateTransform(angleDegrees);

                // A falling drop smears out behind itself: the tail fades out towards
                // the top, the leading edge carries a bright head.
                var transparent = Color.FromArgb(0, 226, 240, 255);
                var soft = Color.FromArgb(ToByte(alpha * 0.32f), 232, 244, 255);
                var head = Color.FromArgb(ToByte(alpha * 0.95f), 248, 252, 255);

                using (var brush = new LinearGradientBrush(
                    new RectangleF(-softWidth / 2f, 0, softWidth, length),
                    transparent, head, LinearGradientMode.Vertical))
                {
                    brush.InterpolationColors = new ColorBlend(4)
                    {
                        Colors = new[] { transparent, soft, Color.FromArgb(ToByte(alpha * 0.92f), 246, 251, 255), head },
                        Positions = new[] { 0.0f, 0.45f, 0.8f, 1.0f }
                    };
                    g.FillRectangle(brush, -softWidth / 2f, 0, softWidth, length);
                }

                // bright core
                using (var brush = new LinearGradientBrush(
                    new RectangleF(-1f, 0, 2f, length),
                    transparent, transparent, LinearGradientMode.Vertical))
                {
                    brush.InterpolationColors = new ColorBlend(4)
                    {
                        Colors = new[]
                        {
                            Color.FromArgb(0, 255, 255, 255),
                            Color.FromArgb(ToByte(alpha * 0.5f), 255, 255, 255),
                            Color.FromArgb(ToByte(alpha), 255, 255, 255),
                            Color.FromArgb(ToByte(alpha), 255, 255, 255)
                        },
                        Positions = new[] { 0.0f, 0.5f, 0.88f, 1.0f }
                    };
                    g.FillRectangle(brush, -1f, 0, 2f, length);
                }
            }

            return bmp;
        }

        /// <summary>
        /// Water droplet: soft body, darker rim and a specular highlight,
        /// readable on top of any wallpaper.
        /// </summary>
        private static Bitmap CreateDrop(int radius, float alpha)
        {
            int size = radius * 2 + 12;
            var bmp = CreateCanvas(size, size);
            float cx = size / 2f - 0.5f;
            float cy = size / 2f - 0.5f;
            float rx = radius;
            float ry = radius * 1.12f;

            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;

                var body = new RectangleF(cx - rx, cy - ry, rx * 2, ry * 2);

                // very subtle body, a lens shaped droplet should not read as a ring
                using (var path = new GraphicsPath())
                {
                    path.AddEllipse(body);
                    using (var brush = new PathGradientBrush(path))
                    {
                        brush.CenterPoint = new PointF(cx - rx * 0.3f, cy - ry * 0.35f);
                        brush.CenterColor = Color.FromArgb(ToByte(alpha * 0.16f), 250, 253, 255);
                        brush.SurroundColors = new[]
                        {
                            Color.FromArgb(ToByte(alpha * 0.05f), 200, 220, 240)
                        };
                        g.FillPath(brush, path);
                    }
                }

                // bevel: short dark lower right arc and short bright upper left arc
                float thickness = Math.Max(1f, radius * 0.15f);
                using (var pen = new Pen(Color.FromArgb(ToByte(alpha * 0.26f), 60, 84, 112), thickness))
                {
                    g.DrawArc(pen, body, 25f, 120f);
                }
                using (var pen = new Pen(Color.FromArgb(ToByte(alpha * 0.3f), 255, 255, 255), Math.Max(1f, thickness * 0.85f)))
                {
                    g.DrawArc(pen, body, 205f, 110f);
                }

                // specular highlight (upper left), the feature that reads as water
                float hl = Math.Max(1.8f, radius * 0.7f);
                using (var path = new GraphicsPath())
                {
                    path.AddEllipse(cx - rx * 0.34f - hl / 2f, cy - ry * 0.42f - hl / 2f, hl, hl * 0.78f);
                    using (var brush = new PathGradientBrush(path))
                    {
                        brush.CenterColor = Color.FromArgb(ToByte(alpha * 0.92f), 255, 255, 255);
                        brush.SurroundColors = new[] { Color.FromArgb(0, 255, 255, 255) };
                        g.FillPath(brush, path);
                    }
                }

                // refracted light at the bottom right
                float glint = Math.Max(1.4f, radius * 0.45f);
                using (var path = new GraphicsPath())
                {
                    path.AddEllipse(cx + rx * 0.22f, cy + ry * 0.36f, glint, glint * 0.5f);
                    using (var brush = new PathGradientBrush(path))
                    {
                        brush.CenterColor = Color.FromArgb(ToByte(alpha * 0.34f), 255, 255, 255);
                        brush.SurroundColors = new[] { Color.FromArgb(0, 255, 255, 255) };
                        g.FillPath(brush, path);
                    }
                }
            }

            return bmp;
        }

        /// <summary>Expanding impact ring.</summary>
        private static Bitmap CreateRipple(int diameter, float alpha)
        {
            int size = diameter + 8;
            var bmp = CreateCanvas(size, size);
            float cx = size / 2f - 0.5f;
            float cy = size / 2f - 0.5f;
            float radius = diameter / 2f - 1f;

            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                float ring = Math.Max(1f, diameter * 0.075f);
                using (var pen = new Pen(Color.FromArgb(ToByte(alpha * 0.75f), 240, 248, 255), ring))
                {
                    g.DrawEllipse(pen, cx - radius, cy - radius, radius * 2, radius * 2);
                }
                using (var pen = new Pen(Color.FromArgb(ToByte(alpha * 0.35f), 255, 255, 255), Math.Max(1f, ring * 2.2f)))
                {
                    g.DrawEllipse(pen, cx - radius, cy - radius, radius * 2, radius * 2);
                }
            }

            return bmp;
        }

        /// <summary>Thin wet trail left behind by a sliding droplet.</summary>
        private static Bitmap CreateTrail(int length, float alpha)
        {
            var bmp = CreateCanvas(9, length + 4);
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                var top = Color.FromArgb(0, 235, 245, 255);
                var bottom = Color.FromArgb(ToByte(alpha * 0.75f), 225, 240, 255);
                using (var brush = new LinearGradientBrush(
                    new RectangleF(3.5f, 0, 2f, length), top, bottom, LinearGradientMode.Vertical))
                {
                    g.FillRectangle(brush, 3.5f, 0, 2f, length);
                }
                using (var brush = new LinearGradientBrush(
                    new RectangleF(2.5f, 0, 4f, length), Color.FromArgb(0, 255, 255, 255), Color.FromArgb(ToByte(alpha * 0.25f), 255, 255, 255), LinearGradientMode.Vertical))
                {
                    g.FillRectangle(brush, 2.5f, 0, 4f, length);
                }
            }
            return bmp;
        }

        /// <summary>Small bright splash highlight.</summary>
        private static Bitmap CreateSparkle(int radius, float alpha)
        {
            int size = radius * 2 + 6;
            var bmp = CreateCanvas(size, size);
            float cx = size / 2f - 0.5f;
            float cy = size / 2f - 0.5f;
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                using (var path = new GraphicsPath())
                {
                    path.AddEllipse(cx - radius, cy - radius, radius * 2, radius * 2);
                    using (var brush = new PathGradientBrush(path))
                    {
                        brush.CenterColor = Color.FromArgb(ToByte(alpha), 255, 255, 255);
                        brush.SurroundColors = new[] { Color.FromArgb(0, 255, 255, 255) };
                        g.FillPath(brush, path);
                    }
                }
            }
            return bmp;
        }

        private static int ToByte(float value)
        {
            int v = (int)Math.Round(value * 255f);
            if (v < 0) return 0;
            if (v > 255) return 255;
            return v;
        }
    }
}

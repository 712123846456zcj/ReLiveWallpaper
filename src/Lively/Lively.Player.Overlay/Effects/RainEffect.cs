using System;
using System.Drawing;

namespace Lively.Player.Overlay.Effects
{
    /// <summary>
    /// Rain over the wallpaper: falling streaks, droplets resting on the "glass"
    /// with the occasional slide and impact ripples.
    /// </summary>
    internal sealed class RainEffect : IEffect
    {
        private const int MaxStreaks = 1400;
        private const int MaxDrops = 900;
        private const int MaxRipples = 260;
        private const int MaxSparkles = 160;

        private struct Streak
        {
            public float X;
            public float Y;
            public int LengthIndex;
            public float Alpha;
            public float Speed;
        }

        private struct GlassDrop
        {
            public float X;
            public float Y;
            public float StartY;
            public int RadiusIndex;
            public float Alpha;
            public float SlideSpeed;
            public bool IsSliding;
            public float TrailLength;
        }

        private struct Ripple
        {
            public float X;
            public float Y;
            public float Age;
            public float Lifetime;
            public float MaxDiameter;
            public float Opacity;
        }

        private struct Sparkle
        {
            public float X;
            public float Y;
            public float Age;
            public float Lifetime;
            public int SizeIndex;
            public float Opacity;
        }

        private readonly Random random = new Random();
        private readonly Streak[] streaks = new Streak[MaxStreaks];
        private readonly GlassDrop[] drops = new GlassDrop[MaxDrops];
        private readonly Ripple[] ripples = new Ripple[MaxRipples];
        private readonly Sparkle[] sparkles = new Sparkle[MaxSparkles];

        private int streakCount;
        private int dropCount;
        private int rippleCount;
        private int sparkleCount;
        private double rippleBudget;
        private int width;
        private int height;
        private bool seeded;

        public string Id => "rain";
        public int FrameIntervalMs { get; private set; } = 33;

        public void Render(EffectContext context)
        {
            var properties = context.Properties ?? new EffectProperties();
            var intensity = properties.GetNumberClamped("intensity", 1.0, 0.05, 3.0);
            var speed = properties.GetNumberClamped("speed", 1.0, 0.05, 4.0);
            var wind = properties.GetNumberClamped("wind", 0.12, -1.0, 1.0);
            var dropScale = properties.GetNumberClamped("dropSize", 1.0, 0.4, 2.5);
            var opacity = properties.GetNumberClamped("opacity", 1.0, 0.05, 1.0);
            var glassDrops = properties.GetFlag("drops", true);
            var sliding = properties.GetFlag("sliding", true);
            var ripplesEnabled = properties.GetFlag("ripples", true);
            var fps = properties.GetNumberClamped("fps", 30, 5, 144);

            FrameIntervalMs = (int)Math.Round(1000.0 / fps);

            var dt = Math.Min(context.DeltaSeconds, 0.1);
            if (dt <= 0)
                dt = 1.0 / 60.0;

            var areaScale = Math.Sqrt((context.Width * (double)context.Height) / (1920.0 * 1080.0));
            areaScale = Math.Max(0.5, Math.Min(2.0, areaScale));

            streakCount = Clamp((int)(260 * intensity * areaScale), 0, MaxStreaks);
            dropCount = glassDrops ? Clamp((int)(120 * intensity * areaScale), 0, MaxDrops) : 0;
            var rippleRate = ripplesEnabled ? 5.0 * intensity * areaScale : 0.0;

            Seed(context.Width, context.Height, dropScale, opacity, sliding);

            var angleIndex = StreakAngleIndex(wind);
            var fallSpeed = (float)(context.Height * 0.95 * speed);
            var drift = (float)(wind * 220.0 * speed);

            // falling rain
            for (int i = 0; i < streakCount; i++)
            {
                var s = streaks[i];
                s.Y += fallSpeed * s.Speed * (float)dt;
                s.X += drift * s.Speed * (float)dt;
                if (s.Y > context.Height + 8 || s.X < -260 || s.X > context.Width + 260)
                    SpawnStreak(ref s, opacity, entering: false);
                streaks[i] = s;
            }

            // droplets resting on the glass
            for (int i = 0; i < dropCount; i++)
            {
                var d = drops[i];
                if (d.IsSliding)
                {
                    d.Y += d.SlideSpeed * (float)dt;
                    d.TrailLength = Math.Max(0f, d.Y - d.StartY);
                    if (d.Y > context.Height + 24)
                        SpawnDrop(ref d, dropScale, opacity, sliding, onGlass: false);
                }
                else if (sliding && random.NextDouble() < 0.05 * dt)
                {
                    d.IsSliding = true;
                    d.StartY = d.Y;
                    d.TrailLength = 0f;
                    d.SlideSpeed = 40f + (float)random.NextDouble() * 70f + d.RadiusIndex * 16f;
                }
                drops[i] = d;
            }

            // impact ripples
            rippleBudget += rippleRate * dt;
            var spawnGuard = 0;
            while (rippleBudget >= 1.0 && spawnGuard++ < 32)
            {
                rippleBudget -= 1.0;
                SpawnRipple(context.Width, context.Height, dropScale, opacity);
            }

            for (int i = 0; i < rippleCount; i++)
            {
                var r = ripples[i];
                r.Age += (float)dt;
                if (r.Age >= r.Lifetime)
                {
                    ripples[i] = ripples[rippleCount - 1];
                    rippleCount--;
                    i--;
                    continue;
                }
                ripples[i] = r;
            }

            for (int i = 0; i < sparkleCount; i++)
            {
                var s = sparkles[i];
                s.Age += (float)dt;
                if (s.Age >= s.Lifetime)
                {
                    sparkles[i] = sparkles[sparkleCount - 1];
                    sparkleCount--;
                    i--;
                    continue;
                }
                sparkles[i] = s;
            }

            Draw(context.Graphics, angleIndex);
        }

        private void Draw(Graphics g, int angleIndex)
        {
            // rain streaks, behind the glass
            for (int i = 0; i < streakCount; i++)
            {
                var s = streaks[i];
                var sprite = SpriteLibrary.GetStreak(angleIndex, s.LengthIndex, s.Alpha);
                g.DrawImageUnscaled(sprite, (int)Math.Round(s.X), (int)Math.Round(s.Y));
            }

            for (int i = 0; i < rippleCount; i++)
            {
                var r = ripples[i];
                var progress = r.Age / r.Lifetime;
                var diameter = r.MaxDiameter * (float)Math.Sqrt(progress);
                var index = RippleIndex(diameter);
                var alpha = r.Opacity * (1f - progress) * 0.9f;
                if (alpha <= 0.02f)
                    continue;
                var sprite = SpriteLibrary.GetRipple(index, alpha);
                g.DrawImageUnscaled(sprite, (int)Math.Round(r.X - sprite.Width / 2.0), (int)Math.Round(r.Y - sprite.Height / 2.0));
            }

            for (int i = 0; i < sparkleCount; i++)
            {
                var s = sparkles[i];
                var progress = s.Age / s.Lifetime;
                var alpha = s.Opacity * (1f - progress);
                if (alpha <= 0.02f)
                    continue;
                var sprite = SpriteLibrary.GetSparkle(s.SizeIndex, alpha);
                g.DrawImageUnscaled(sprite, (int)Math.Round(s.X - sprite.Width / 2.0), (int)Math.Round(s.Y - sprite.Height / 2.0));
            }

            // droplets in front of the glass
            for (int i = 0; i < dropCount; i++)
            {
                var d = drops[i];
                if (d.IsSliding && d.TrailLength > 6f)
                    DrawTrail(g, d);
                var sprite = SpriteLibrary.GetDrop(d.RadiusIndex, d.Alpha);
                g.DrawImageUnscaled(sprite, (int)Math.Round(d.X - sprite.Width / 2.0), (int)Math.Round(d.Y - sprite.Height / 2.0));
            }
        }

        private void DrawTrail(Graphics g, GlassDrop drop)
        {
            var remaining = drop.TrailLength;
            var y = drop.Y - 6f;
            var guard = 0;
            while (remaining > 6f && guard++ < 10)
            {
                var index = NearestIndex(SpriteLibrary.TrailLengths, remaining);
                var sprite = SpriteLibrary.GetTrail(index, drop.Alpha * 0.45f);
                y -= sprite.Height - 4;
                g.DrawImageUnscaled(sprite, (int)Math.Round(drop.X - sprite.Width / 2.0), (int)Math.Round(y));
                remaining -= sprite.Height - 4;
            }
        }

        private void Seed(int width, int height, double dropScale, double opacity, bool sliding)
        {
            var resized = seeded && (this.width != width || this.height != height);
            if (seeded && !resized)
                return;

            this.width = width;
            this.height = height;

            for (int i = 0; i < MaxStreaks; i++)
            {
                var s = streaks[i];
                if (!seeded)
                    SpawnStreak(ref s, opacity, entering: true);
                else
                {
                    s.X = (float)(random.NextDouble() * width);
                    s.Y = (float)(random.NextDouble() * height);
                }
                streaks[i] = s;
            }

            for (int i = 0; i < MaxDrops; i++)
            {
                var d = drops[i];
                if (!seeded)
                    SpawnDrop(ref d, dropScale, opacity, sliding, onGlass: true);
                else
                {
                    d.X = (float)(random.NextDouble() * width);
                    d.Y = (float)(random.NextDouble() * height);
                    d.StartY = d.Y;
                }
                drops[i] = d;
            }

            if (!seeded)
                PlaceDropClusters();

            seeded = true;
        }

        /// <summary>Spray a few groups of droplets so the glass does not look uniform.</summary>
        private void PlaceDropClusters()
        {
            var clusterCount = Math.Max(1, MaxDrops / 24);
            for (int c = 0; c < clusterCount; c++)
            {
                var centerX = random.NextDouble() * width;
                var centerY = random.NextDouble() * height;
                var members = 2 + random.Next(6);
                for (int m = 0; m < members; m++)
                {
                    var index = random.Next(MaxDrops);
                    var d = drops[index];
                    d.X = (float)ClampToRange(Gaussian() * 70 + centerX, 0, width);
                    d.Y = (float)ClampToRange(Gaussian() * 70 + centerY, 0, height);
                    d.StartY = d.Y;
                    drops[index] = d;
                }
            }
        }

        private double Gaussian()
            => (random.NextDouble() + random.NextDouble() + random.NextDouble() - 1.5) * 0.9;

        private static double ClampToRange(double value, double min, double max)
            => value < min ? min : (value > max ? max : value);

        private void SpawnStreak(ref Streak s, double opacity, bool entering)
        {
            s.X = (float)(random.NextDouble() * (width + 240) - 120);
            s.Y = entering
                ? (float)(random.NextDouble() * height)
                : (float)(-random.NextDouble() * height * 0.35 - 20);
            s.LengthIndex = WeightedLengthIndex();
            s.Alpha = (float)((0.3 + random.NextDouble() * 0.7) * opacity);
            s.Speed = 0.75f + (float)random.NextDouble() * 0.65f;
        }

        private int WeightedLengthIndex()
        {
            var roll = random.NextDouble();
            if (roll < 0.35) return 0;
            if (roll < 0.6) return 1;
            if (roll < 0.78) return 2;
            if (roll < 0.9) return 3;
            if (roll < 0.97) return 4;
            return 5;
        }

        private void SpawnDrop(ref GlassDrop d, double dropScale, double opacity, bool sliding, bool onGlass)
        {
            d.X = (float)(random.NextDouble() * width);
            d.Y = onGlass
                ? (float)(random.NextDouble() * height)
                : (float)(random.NextDouble() * height * 0.2 - 10);
            d.StartY = d.Y;
            // bias towards small droplets, big ones are rare
            var shaped = Math.Pow(random.NextDouble(), 2.2) * dropScale;
            d.RadiusIndex = Clamp((int)Math.Round(shaped * (SpriteLibrary.DropRadii.Length - 1)), 0, SpriteLibrary.DropRadii.Length - 1);
            d.Alpha = (float)((0.45 + random.NextDouble() * 0.55) * opacity);
            d.IsSliding = false;
            d.TrailLength = 0f;
            d.SlideSpeed = 0f;
        }

        private void SpawnRipple(int width, int height, double dropScale, double opacity)
        {
            if (rippleCount >= MaxRipples)
                return;

            var r = new Ripple
            {
                X = (float)(random.NextDouble() * width),
                Y = (float)(random.NextDouble() * height),
                Age = 0f,
                Lifetime = 0.45f + (float)random.NextDouble() * 0.45f,
                MaxDiameter = (float)((14 + random.NextDouble() * 24) * Math.Min(1.6, dropScale)),
                Opacity = (float)((0.4 + random.NextDouble() * 0.6) * opacity)
            };
            ripples[rippleCount++] = r;

            if (sparkleCount < MaxSparkles && random.NextDouble() < 0.6)
            {
                sparkles[sparkleCount++] = new Sparkle
                {
                    X = r.X,
                    Y = r.Y,
                    Age = 0f,
                    Lifetime = 0.18f + (float)random.NextDouble() * 0.12f,
                    SizeIndex = random.Next(2),
                    Opacity = (float)((0.5 + random.NextDouble() * 0.5) * opacity)
                };
            }
        }

        private static int StreakAngleIndex(double wind)
        {
            var index = (int)Math.Round(SpriteLibrary.VerticalAngleIndex + wind * (SpriteLibrary.VerticalAngleIndex + 0.4));
            return Clamp(index, 0, SpriteLibrary.StreakAngles.Length - 1);
        }

        private static int RippleIndex(float diameter)
        {
            var index = 0;
            for (int i = 0; i < SpriteLibrary.RippleDiameters.Length; i++)
            {
                if (SpriteLibrary.RippleDiameters[i] <= diameter)
                    index = i;
            }
            return index;
        }

        private static int NearestIndex(int[] values, float value)
        {
            var best = 0;
            var bestDelta = float.MaxValue;
            for (int i = 0; i < values.Length; i++)
            {
                var delta = Math.Abs(values[i] - value);
                if (delta < bestDelta)
                {
                    bestDelta = delta;
                    best = i;
                }
            }
            return best;
        }

        private static int Clamp(int value, int min, int max)
            => value < min ? min : (value > max ? max : value);

        public void Dispose()
        {
        }
    }
}

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.Json;

namespace Lively.Player.Overlay.Effects
{
    /// <summary>
    /// Flat key/value bag read from the property json written by the Lively core.
    /// Missing or malformed entries always fall back to the effect defaults so that
    /// a broken file can never stop the effect from rendering.
    /// </summary>
    internal sealed class EffectProperties
    {
        private readonly Dictionary<string, double> numbers = new Dictionary<string, double>();
        private readonly Dictionary<string, bool> flags = new Dictionary<string, bool>();

        public static EffectProperties Load(string path)
        {
            var properties = new EffectProperties();
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                return properties;

            try
            {
                using (var stream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                using (var document = JsonDocument.Parse(stream))
                {
                    if (document.RootElement.ValueKind != JsonValueKind.Object)
                        return properties;

                    foreach (var item in document.RootElement.EnumerateObject())
                    {
                        switch (item.Value.ValueKind)
                        {
                            case JsonValueKind.Number:
                                properties.numbers[item.Name] = item.Value.GetDouble();
                                break;
                            case JsonValueKind.True:
                                properties.flags[item.Name] = true;
                                break;
                            case JsonValueKind.False:
                                properties.flags[item.Name] = false;
                                break;
                            case JsonValueKind.String:
                                if (double.TryParse(item.Value.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed))
                                    properties.numbers[item.Name] = parsed;
                                else if (bool.TryParse(item.Value.GetString(), out var parsedFlag))
                                    properties.flags[item.Name] = parsedFlag;
                                break;
                        }
                    }
                }
            }
            catch
            {
                // Fall back to defaults.
            }

            return properties;
        }

        public double GetNumber(string key, double fallback)
            => numbers.TryGetValue(key, out var value) ? value : fallback;

        public bool GetFlag(string key, bool fallback)
            => flags.TryGetValue(key, out var value) ? value : fallback;

        public double GetNumberClamped(string key, double fallback, double min, double max)
        {
            var value = GetNumber(key, fallback);
            if (double.IsNaN(value) || double.IsInfinity(value))
                return fallback;
            if (value < min) return min;
            if (value > max) return max;
            return value;
        }
    }
}

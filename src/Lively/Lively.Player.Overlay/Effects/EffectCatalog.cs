using System;
using System.Collections.Generic;

namespace Lively.Player.Overlay.Effects
{
    /// <summary>
    /// Maps an effect id to its renderer. Adding an effect means adding a class
    /// implementing <see cref="IEffect"/> and relying it here.
    /// </summary>
    internal static class EffectCatalog
    {
        public static readonly IReadOnlyList<string> Effects = new[] { "rain" };

        public static IEffect Create(string id)
        {
            switch ((id ?? string.Empty).Trim().ToLowerInvariant())
            {
                case "rain":
                    return new RainEffect();
                default:
                    return null;
            }
        }
    }
}

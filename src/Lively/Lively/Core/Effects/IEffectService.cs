using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Lively.Core.Effects
{
    /// <summary>
    /// Overlay effects rendered on top of the desktop wallpaper, independently of the
    /// wallpaper player, so that any wallpaper type can be combined with them.
    /// </summary>
    public interface IEffectService : IDisposable
    {
        /// <summary>All effects, available and planned ones.</summary>
        IReadOnlyList<EffectInfo> GetEffects();

        /// <summary>Starts the effects that were enabled during the previous session.</summary>
        void Start();

        Task SetEnabledAsync(string effectId, bool isEnabled);

        /// <summary>Updates a single value of the effect properties file.</summary>
        Task SetPropertyAsync(string effectId, string key, string value);

        /// <summary>Restores the effect properties from the bundled template.</summary>
        Task ResetPropertiesAsync(string effectId);

        /// <summary>Raised when an effect is toggled, the user interface refreshes.</summary>
        event EventHandler EffectsChanged;
    }
}

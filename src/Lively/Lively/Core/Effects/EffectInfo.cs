using System;

namespace Lively.Core.Effects
{
    /// <summary>
    /// A desktop overlay effect as reported to the user interface.
    /// </summary>
    public class EffectInfo
    {
        public EffectInfo(string id, bool isAvailable, string propertyPath, bool isEnabled)
        {
            Id = id;
            IsAvailable = isAvailable;
            PropertyPath = propertyPath;
            IsEnabled = isEnabled;
        }

        public string Id { get; }

        /// <summary>False for effects that are planned but have no renderer yet.</summary>
        public bool IsAvailable { get; }

        /// <summary>User editable copy of the effect properties, null when not available.</summary>
        public string PropertyPath { get; }

        public bool IsEnabled { get; }
    }
}

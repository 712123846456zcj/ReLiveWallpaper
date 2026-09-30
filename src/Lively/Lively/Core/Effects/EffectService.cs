using Lively.Common;
using Lively.Common.Helpers;
using Lively.Common.Services;
using Lively.Core.Display;
using Lively.Core.Suspend;
using Lively.Models;
using Lively.Models.Message;
using Newtonsoft.Json;
using NLog;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace Lively.Core.Effects
{
    /// <summary>
    /// Owns the overlay effect players, one process per display and enabled effect.
    ///
    /// The overlays are independent of the wallpaper player, they always sit directly
    /// above the desktop window, that way any wallpaper (web, media, third party
    /// engines..) can be combined with an effect.
    /// </summary>
    internal sealed class EffectService : IEffectService
    {
        /// <summary>Effects offered in the interface that have no renderer yet.</summary>
        private static readonly string[] PlannedEffects = new string[] { "smoke", "sparks" };
        private const string PropertyFileName = "LivelyProperties.json";
        private const string LocalizationFileName = "LivelyProperties.loc.json";
        private const string StateFileName = "state.json";

        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        private readonly IUserSettingsService userSettings;
        private readonly IDisplayManager displayManager;
        private readonly IPlayback playback;
        private readonly object sync = new object();
        private readonly List<EffectOverlayPlayer> players = new List<EffectOverlayPlayer>();
        private readonly HashSet<string> enabledEffects = new HashSet<string>();
        private List<string> displayLayout = new List<string>();
        private bool disposed;

        public EffectService(IUserSettingsService userSettings,
            IDisplayManager displayManager,
            IPlayback playback)
        {
            this.userSettings = userSettings;
            this.displayManager = displayManager;
            this.playback = playback;

            this.displayManager.DisplayUpdated += DisplayManager_DisplayUpdated;
            this.playback.WallpaperControlChanged += Playback_WallpaperControlChanged;
        }

        public event EventHandler EffectsChanged;

        private static string TemplateRoot => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", "Plugins", "Overlay");

        private static string PlayerExePath => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, Constants.PlayerPartialPaths.OverlayPath);

        private string DataDirectory => Path.Combine(userSettings.Settings.WallpaperDir, Constants.CommonPartialPaths.EffectsSettingsDir);

        private string StatePath => Path.Combine(DataDirectory, StateFileName);

        public IReadOnlyList<EffectInfo> GetEffects()
        {
            var result = new List<EffectInfo>();
            foreach (var effectId in GetAvailableEffects())
            {
                // Create the user editable copy up front, the interface reads the defaults from it.
                SyncProperties(effectId);
                result.Add(new EffectInfo(effectId, true, GetPropertyPath(effectId), IsEnabled(effectId)));
            }

            foreach (var effectId in PlannedEffects)
            {
                if (!result.Any(x => string.Equals(x.Id, effectId, StringComparison.OrdinalIgnoreCase)))
                    result.Add(new EffectInfo(effectId, false, null, false));
            }

            return result;
        }

        public void Start()
        {
            try
            {
                LoadState();
                foreach (var effectId in enabledEffects.ToList())
                {
                    if (!GetAvailableEffects().Contains(effectId, StringComparer.OrdinalIgnoreCase))
                        continue;

                    SyncProperties(effectId);
                    StartEffect(effectId);
                }
            }
            catch (Exception e)
            {
                Logger.Error(e);
            }
        }

        public async Task SetEnabledAsync(string effectId, bool isEnabled)
        {
            if (string.IsNullOrWhiteSpace(effectId))
                return;

            effectId = effectId.Trim().ToLowerInvariant();

            bool changed;
            lock (sync)
            {
                changed = isEnabled ? enabledEffects.Add(effectId) : enabledEffects.Remove(effectId);
                if (changed)
                    SaveState();
            }

            if (!changed)
                return;

            if (isEnabled)
            {
                SyncProperties(effectId);
                StartEffect(effectId);
            }
            else
            {
                await StopEffectAsync(effectId);
            }

            EffectsChanged?.Invoke(this, EventArgs.Empty);
        }

        public Task SetPropertyAsync(string effectId, string key, string value)
        {
            if (string.IsNullOrWhiteSpace(effectId) || string.IsNullOrWhiteSpace(key))
                return Task.CompletedTask;

            var propertyPath = GetPropertyPath(effectId);
            if (string.IsNullOrEmpty(propertyPath))
                return Task.CompletedTask;

            if (!LivelyPropertyUtil.SetPropertyValue(propertyPath, key, value))
            {
                Logger.Error($"Effect '{effectId}': Failed to set property '{key}'.");
                return Task.CompletedTask;
            }

            Reload(effectId);
            return Task.CompletedTask;
        }

        public Task ResetPropertiesAsync(string effectId)
        {
            if (string.IsNullOrWhiteSpace(effectId))
                return Task.CompletedTask;

            var propertyPath = Path.Combine(DataDirectory, effectId, PropertyFileName);
            try
            {
                if (File.Exists(propertyPath))
                    File.Delete(propertyPath);
            }
            catch (Exception e)
            {
                Logger.Error(e);
            }

            SyncProperties(effectId);
            Reload(effectId);
            return Task.CompletedTask;
        }

        public bool IsEnabled(string effectId)
        {
            lock (sync)
            {
                return enabledEffects.Contains(effectId);
            }
        }

        /// <summary>Effect ids that have a bundled property template.</summary>
        private IReadOnlyList<string> GetAvailableEffects()
        {
            try
            {
                if (Directory.Exists(TemplateRoot))
                {
                    var effects = Directory.GetDirectories(TemplateRoot)
                        .Where(x => File.Exists(Path.Combine(x, PropertyFileName)))
                        .Select(x => new DirectoryInfo(x).Name)
                        .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                        .ToList();

                    if (effects.Count != 0)
                        return effects;
                }
            }
            catch (Exception e)
            {
                Logger.Error(e);
            }

            return new List<string>() { "rain" };
        }

        /// <summary>Path of the user editable copy of the effect properties.</summary>
        private string GetPropertyPath(string effectId)
        {
            var path = Path.Combine(DataDirectory, effectId, PropertyFileName);
            return File.Exists(path) ? path : null;
        }

        /// <summary>Creates or updates the user copy of the effect properties.</summary>
        private void SyncProperties(string effectId)
        {
            try
            {
                var templateDirectory = Path.Combine(TemplateRoot, effectId);
                var dataDirectory = Path.Combine(DataDirectory, effectId);
                LivelyPropertyUtil.SyncPropertyFile(Path.Combine(templateDirectory, PropertyFileName), dataDirectory);
                LivelyPropertyUtil.SyncPropertyFile(Path.Combine(templateDirectory, LocalizationFileName), dataDirectory, LocalizationFileName);
            }
            catch (Exception e)
            {
                Logger.Error(e);
            }
        }

        private void StartEffect(string effectId)
        {
            if (disposed)
                return;

            var exePath = PlayerExePath;
            if (!File.Exists(exePath))
            {
                Logger.Error($"Effect '{effectId}': Overlay player not found at {exePath}");
                return;
            }

            var propertyPath = GetPropertyPath(effectId);

            lock (sync)
            {
                if (players.Any(x => string.Equals(x.EffectId, effectId, StringComparison.OrdinalIgnoreCase)))
                    return;

                foreach (var display in displayManager.DisplayMonitors.ToList())
                {
                    var player = new EffectOverlayPlayer(exePath, effectId, propertyPath, display);
                    player.Exited += Player_Exited;
                    player.Show();
                    players.Add(player);
                }
            }
        }

        private async Task StopEffectAsync(string effectId)
        {
            List<EffectOverlayPlayer> items;
            lock (sync)
            {
                items = players.Where(x => string.Equals(x.EffectId, effectId, StringComparison.OrdinalIgnoreCase)).ToList();
                foreach (var item in items)
                    players.Remove(item);
            }

            foreach (var item in items)
            {
                item.Exited -= Player_Exited;
                await item.CloseAsync();
                item.Dispose();
            }
        }

        private void Reload(string effectId)
        {
            foreach (var player in Snapshot(effectId))
                player.Send(new LivelyReloadCmd());
        }

        private List<EffectOverlayPlayer> Snapshot(string effectId = null)
        {
            lock (sync)
            {
                return players
                    .Where(x => effectId is null || string.Equals(x.EffectId, effectId, StringComparison.OrdinalIgnoreCase))
                    .ToList();
            }
        }

        /// <summary>Monitor identity and bounds, used to detect a real display change.</summary>
        private List<string> GetDisplayLayout()
        {
            return displayManager.DisplayMonitors
                .Select(x => string.Format(CultureInfo.InvariantCulture, "{0}:{1}", x.DeviceId, x.Bounds))
                .OrderBy(x => x, StringComparer.Ordinal)
                .ToList();
        }

        private void Player_Exited(object sender, EventArgs e)
        {
            if (sender is not EffectOverlayPlayer player)
                return;

            lock (sync)
            {
                players.Remove(player);
            }
        }

        /// <summary>The desktop is covered or the wallpaper paused, stop drawing to save power.</summary>
        private void Playback_WallpaperControlChanged(object sender, WallpaperControlEventArgs e)
        {
            if (e.Action != WallpaperControlAction.Pause && e.Action != WallpaperControlAction.Play)
                return;

            var message = e.Action == WallpaperControlAction.Pause
                ? (IpcMessage)new LivelySuspendCmd()
                : new LivelyResumeCmd();

            foreach (var player in Snapshot())
            {
                if (e.Display is not null && !e.Display.Equals(player.Screen))
                    continue;

                player.Send(message);
            }
        }

        private void DisplayManager_DisplayUpdated(object sender, EventArgs e)
        {
            if (!Snapshot().Any())
                return;

            // The shell refreshes the monitor list for unrelated reasons (interface start..),
            // only rebuild the overlays when the layout actually changed.
            var layout = GetDisplayLayout();
            lock (sync)
            {
                if (layout.SequenceEqual(displayLayout))
                    return;

                displayLayout = layout;
            }

            _ = Task.Run(async () =>
            {
                try
                {
                    var effectIds = Snapshot().Select(x => x.EffectId).Distinct().ToList();
                    foreach (var effectId in effectIds)
                        await StopEffectAsync(effectId);

                    foreach (var effectId in effectIds)
                        StartEffect(effectId);
                }
                catch (Exception ex)
                {
                    Logger.Error(ex);
                }
            });
        }

        private void LoadState()
        {
            try
            {
                lock (sync)
                {
                    enabledEffects.Clear();
                    if (!File.Exists(StatePath))
                        return;

                    var state = JsonConvert.DeserializeObject<EffectStateModel>(File.ReadAllText(StatePath));
                    if (state?.Enabled is null)
                        return;

                    foreach (var effectId in state.Enabled)
                    {
                        if (!string.IsNullOrWhiteSpace(effectId))
                            enabledEffects.Add(effectId.Trim().ToLowerInvariant());
                    }
                }
            }
            catch (Exception e)
            {
                Logger.Error(e);
            }
        }

        private void SaveState()
        {
            try
            {
                Directory.CreateDirectory(DataDirectory);
                var state = new EffectStateModel() { Enabled = enabledEffects.OrderBy(x => x).ToList() };
                File.WriteAllText(StatePath, JsonConvert.SerializeObject(state, Formatting.Indented));
            }
            catch (Exception e)
            {
                Logger.Error(e);
            }
        }

        public void Dispose()
        {
            if (disposed)
                return;

            disposed = true;
            displayManager.DisplayUpdated -= DisplayManager_DisplayUpdated;
            playback.WallpaperControlChanged -= Playback_WallpaperControlChanged;

            foreach (var player in Snapshot())
            {
                player.Exited -= Player_Exited;
                player.Send(new LivelyCloseCmd());
                player.Dispose();
            }

            lock (sync)
            {
                players.Clear();
            }
        }

        private class EffectStateModel
        {
            [JsonProperty("enabled")]
            public List<string> Enabled { get; set; }
        }
    }
}

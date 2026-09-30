using Lively.Models;
using Lively.Models.Enums;
using Lively.Models.Message;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading.Tasks;

namespace Lively.Grpc.Client
{
    public interface IDesktopCoreClient : IDisposable
    {
        ReadOnlyCollection<WallpaperData> Wallpapers { get; }
        string BaseDirectory { get; }
        Version AssemblyVersion { get; }
        bool IsCoreInitialized { get; }

        Task CloseAllWallpapers();
        Task CloseWallpaper(DisplayMonitor monitor);
        Task CloseWallpaper(LibraryModel item);
        Task CloseWallpaper(WallpaperType type);
        Task SetWallpaper(LibraryModel item, DisplayMonitor display);
        Task SetWallpaper(string livelyInfoPath, string monitorId);
        Task<bool> EditWallpaper(string livelyInfoPath);
        Task<string> CreateWallpaper(string filePath, WallpaperType type, string arguments = null);
        void SendMessageWallpaper(LibraryModel obj, IpcMessage msg);
        void SendMessageWallpaper(DisplayMonitor display, LibraryModel obj, IpcMessage msg);
        Task PreviewWallpaper(string livelyInfoPath);
        Task TakeScreenshot(string monitorId, string savePath);
        Task<List<EffectInfo>> GetEffects();
        Task SetEffect(string effectId, bool isEnabled);
        Task SetEffectProperty(string effectId, string key, string value);
        Task ResetEffectProperties(string effectId);

        event EventHandler WallpaperChanged;
        event EventHandler<Exception> WallpaperError;
    }

    public class WallpaperData
    {
        public string LivelyInfoFolderPath { get; set; }
        public string LivelyPropertyCopyPath { get; set; }
        public string ThumbnailPath { get; set; }
        public string PreviewPath { get; set; }
        public DisplayMonitor Display { get; set; }
        public WallpaperType Category { get; set; }
    }

    /// <summary>Desktop overlay effect, rendered on top of whatever wallpaper is running.</summary>
    public class EffectInfo
    {
        public string Id { get; set; }
        /// <summary>False for effects that are planned but not implemented yet.</summary>
        public bool IsAvailable { get; set; }
        public bool IsEnabled { get; set; }
        /// <summary>User editable properties file, null when the effect is not available.</summary>
        public string PropertyPath { get; set; }
    }

    public class WallpaperUpdatedData
    {
        public LivelyInfoModel Info { get; set; }
        public UpdateWallpaperType Category { get; set; }
        public string InfoPath { get; set; }
    }
}
using Lively.Common;
using Lively.Common.Helpers;
using Lively.Common.Services;
using Lively.Models;
using Lively.Models.Enums;
using System.IO;

namespace Lively.Factories
{
    public class LivelyPropertyFactory : ILivelyPropertyFactory
    {
        public string CreateLivelyPropertyFolder(LibraryModel model, DisplayMonitor display, WallpaperArrangement arrangement, IUserSettingsService userSettings)
        {
            // Customisation not supported.
            if (model.LivelyPropertyPath is null)
                return null;

            string propertyCopyPath = null;
            try
            {
                // Create a directory with the wallpaper foldername in SaveData/wpdata/, copy livelyproperties.json into this.
                // Further modifications are done to the copy file.
                var dataFolder = Path.Combine(userSettings.Settings.WallpaperDir, Constants.CommonPartialPaths.WallpaperSettingsDir);
                var wallpaperDataDirectoryPath = LivelyPropertyUtil.GetPropertyCopyPath(dataFolder, new DirectoryInfo(model.LivelyInfoFolderPath).Name, arrangement, display.Index);
                // Copy the original file if not found, otherwise add properties the app gained since the copy was created.
                propertyCopyPath = LivelyPropertyUtil.SyncPropertyFile(model.LivelyPropertyPath, wallpaperDataDirectoryPath);
            }
            catch { /* Ignore, file related issue so consider wallpaper uncustomisable. */ }

            return propertyCopyPath;
        }
    }
}

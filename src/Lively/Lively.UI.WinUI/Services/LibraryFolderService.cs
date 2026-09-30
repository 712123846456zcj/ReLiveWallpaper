using Lively.Common;
using Lively.Common.Helpers.Storage;
using Lively.Common.Services;
using Lively.Grpc.Client;
using Lively.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Lively.UI.WinUI.Services
{
    public class LibraryFolderService : ILibraryFolderService
    {
        private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();

        private readonly IUserSettingsClient userSettings;

        public LibraryFolderService(IUserSettingsClient userSettings)
        {
            this.userSettings = userSettings;
        }

        /// <inheritdoc/>
        public string FilePath => Path.Combine(userSettings.Settings.WallpaperDir, Constants.CommonPartialPaths.LibraryFoldersFile);

        /// <inheritdoc/>
        public IReadOnlyList<LibraryFolderModel> Load()
        {
            try
            {
                var filePath = FilePath;
                if (!File.Exists(filePath))
                    return Array.Empty<LibraryFolderModel>();

                var folders = JsonStorage<List<LibraryFolderModel>>.LoadData(filePath);
                // Skip entries that cannot be displayed, for instance if the file was hand edited.
                return folders.Where(x => x != null && !string.IsNullOrWhiteSpace(x.Id)).ToList();
            }
            catch (Exception e)
            {
                Logger.Error($"Failed to read library folders: {e.Message}");
                return Array.Empty<LibraryFolderModel>();
            }
        }

        /// <inheritdoc/>
        public void Save(IEnumerable<LibraryFolderModel> folders)
        {
            try
            {
                var filePath = FilePath;
                Directory.CreateDirectory(Path.GetDirectoryName(filePath));
                JsonStorage<List<LibraryFolderModel>>.StoreData(filePath, folders.ToList());
            }
            catch (Exception e)
            {
                Logger.Error($"Failed to write library folders: {e.Message}");
            }
        }
    }
}

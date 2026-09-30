using Lively.Models;
using System.Collections.Generic;

namespace Lively.Common.Services
{
    /// <summary>
    /// Reads and writes user created library folders (wallpaper categorisation.)
    /// </summary>
    public interface ILibraryFolderService
    {
        /// <summary>
        /// Location of the folder database, follows the wallpaper directory setting.
        /// </summary>
        string FilePath { get; }

        /// <summary>
        /// Reads the folders from disk, empty when the file is missing or unreadable.
        /// </summary>
        IReadOnlyList<LibraryFolderModel> Load();

        /// <summary>
        /// Writes the folders to disk, failures are logged and not thrown.
        /// </summary>
        void Save(IEnumerable<LibraryFolderModel> folders);
    }
}

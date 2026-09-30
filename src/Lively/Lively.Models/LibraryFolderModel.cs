using System.Collections.Generic;

namespace Lively.Models
{
    /// <summary>
    /// User created library folder, used to categorise wallpapers.
    /// </summary>
    public class LibraryFolderModel
    {
        /// <summary>
        /// Unique identifier of the folder.
        /// </summary>
        public string Id { get; set; }

        /// <summary>
        /// Display name of the folder.
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// User picked cover image filepath, when unset the cover is picked from the wallpapers contained.
        /// </summary>
        public string CoverImage { get; set; }

        /// <summary>
        /// Directory name of the wallpapers contained in this folder.
        /// </summary>
        public List<string> Wallpapers { get; set; } = new List<string>();
    }
}

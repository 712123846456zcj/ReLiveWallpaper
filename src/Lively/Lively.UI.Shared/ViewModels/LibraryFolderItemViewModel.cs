using CommunityToolkit.Mvvm.ComponentModel;
using Lively.Models;
using System;
using System.Collections.Generic;

namespace Lively.UI.Shared.ViewModels
{
    /// <summary>
    /// Library tile used to group wallpapers, wraps <see cref="LibraryFolderModel"/> with display state.
    /// The root item represents the entire library, it does not filter anything.
    /// </summary>
    public class LibraryFolderItemViewModel : ObservableObject
    {
        private LibraryFolderItemViewModel(string name)
        {
            IsRoot = true;
            Name = name;
            PlaceholderGlyph = "\uE8A9";
        }

        public LibraryFolderItemViewModel(LibraryFolderModel data)
        {
            Data = data;
            Name = data.Name;
        }

        /// <summary>
        /// Tile showing every wallpaper in the library.
        /// </summary>
        public static LibraryFolderItemViewModel CreateRoot(string name) => new LibraryFolderItemViewModel(name);

        /// <summary>
        /// Folder data, null for the root item.
        /// </summary>
        public LibraryFolderModel Data { get; }

        public bool IsRoot { get; }

        public string Id => Data?.Id;

        private string _name;
        public string Name
        {
            get => _name;
            set => SetProperty(ref _name, string.IsNullOrWhiteSpace(value) ? "---" : value);
        }

        private int _wallpaperCount;
        public int WallpaperCount
        {
            get => _wallpaperCount;
            set => SetProperty(ref _wallpaperCount, value);
        }

        /// <summary>
        /// Single image covering the tile, set when a cover is picked or the folder holds one picture.
        /// </summary>
        private string _coverImagePath;
        public string CoverImagePath
        {
            get => _coverImagePath;
            private set => SetProperty(ref _coverImagePath, value);
        }

        /// <summary>
        /// Images of the wallpapers contained, shown Windows folder style.
        /// </summary>
        private IReadOnlyList<string> _coverImages = Array.Empty<string>();
        public IReadOnlyList<string> CoverImages
        {
            get => _coverImages;
            private set => SetProperty(ref _coverImages, value);
        }

        private bool _showCoverImage;
        public bool ShowCoverImage
        {
            get => _showCoverImage;
            private set => SetProperty(ref _showCoverImage, value);
        }

        private bool _showCoverCollage;
        public bool ShowCoverCollage
        {
            get => _showCoverCollage;
            private set => SetProperty(ref _showCoverCollage, value);
        }

        private bool _showCoverPlaceholder;
        public bool ShowCoverPlaceholder
        {
            get => _showCoverPlaceholder;
            private set => SetProperty(ref _showCoverPlaceholder, value);
        }

        private string _placeholderGlyph;
        public string PlaceholderGlyph
        {
            get => _placeholderGlyph;
            private set => SetProperty(ref _placeholderGlyph, value);
        }

        /// <summary>
        /// Shows a single image filling the tile.
        /// </summary>
        public void SetCoverImage(string imagePath)
        {
            CoverImagePath = imagePath;
            CoverImages = Array.Empty<string>();
            ShowCoverImage = !string.IsNullOrWhiteSpace(imagePath);
            ShowCoverCollage = false;
            ShowCoverPlaceholder = !ShowCoverImage;
        }

        /// <summary>
        /// Shows the wallpaper images contained, up to four, Windows folder preview style.
        /// </summary>
        public void SetCoverCollage(IReadOnlyList<string> imagePaths)
        {
            if (imagePaths.Count == 1)
            {
                SetCoverImage(imagePaths[0]);
                return;
            }

            CoverImagePath = null;
            CoverImages = imagePaths;
            ShowCoverImage = false;
            ShowCoverCollage = imagePaths.Count > 1;
            ShowCoverPlaceholder = imagePaths.Count == 0;
        }

        /// <summary>
        /// Shows a glyph when the folder holds nothing to preview.
        /// </summary>
        public void SetCoverPlaceholder(string glyph)
        {
            PlaceholderGlyph = glyph;
            CoverImagePath = null;
            CoverImages = Array.Empty<string>();
            ShowCoverImage = false;
            ShowCoverCollage = false;
            ShowCoverPlaceholder = true;
        }

        public override string ToString() => Name;
    }
}

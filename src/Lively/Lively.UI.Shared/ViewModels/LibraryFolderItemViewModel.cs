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
        /// <summary>
        /// Number of wallpapers used for the tile preview, the count of a folder can be large.
        /// </summary>
        public const int MaxPreviewImages = 8;

        private readonly List<string> previewImages = new();
        private int previewIndex;

        private LibraryFolderItemViewModel(string name)
        {
            IsRoot = true;
            Name = name;
            SetCoverPlaceholder("\uE8A9");
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
        /// Image shown by the tile, alternates between the two slots like the Windows folder preview.
        /// </summary>
        private string _previewSlotA;
        public string PreviewSlotA
        {
            get => _previewSlotA;
            private set => SetProperty(ref _previewSlotA, value);
        }

        private string _previewSlotB;
        public string PreviewSlotB
        {
            get => _previewSlotB;
            private set => SetProperty(ref _previewSlotB, value);
        }

        /// <summary>
        /// Slot opacity, the view animates between the two to cross fade the preview.
        /// </summary>
        private double _previewOpacityA = 1.0;
        public double PreviewOpacityA
        {
            get => _previewOpacityA;
            private set => SetProperty(ref _previewOpacityA, value);
        }

        private double _previewOpacityB;
        public double PreviewOpacityB
        {
            get => _previewOpacityB;
            private set => SetProperty(ref _previewOpacityB, value);
        }

        /// <summary>
        /// Whether the folder holds at least one wallpaper to preview.
        /// </summary>
        public bool ShowCoverImage => previewImages.Count != 0;

        private bool _showCoverPlaceholder = true;
        public bool ShowCoverPlaceholder
        {
            get => _showCoverPlaceholder;
            private set => SetProperty(ref _showCoverPlaceholder, value);
        }

        private string _placeholderGlyph = "\uE8F4";
        public string PlaceholderGlyph
        {
            get => _placeholderGlyph;
            private set => SetProperty(ref _placeholderGlyph, value);
        }

        /// <summary>
        /// Shows the given images, one at a time, the first one is displayed immediately.
        /// </summary>
        public void SetCoverImages(IReadOnlyList<string> imagePaths)
        {
            previewImages.Clear();
            for (int i = 0; i < imagePaths.Count && i < MaxPreviewImages; i++)
            {
                if (!string.IsNullOrWhiteSpace(imagePaths[i]))
                    previewImages.Add(imagePaths[i]);
            }

            previewIndex = 0;
            PreviewSlotA = previewImages.Count != 0 ? previewImages[0] : null;
            PreviewSlotB = null;
            PreviewOpacityA = 1.0;
            PreviewOpacityB = 0.0;
            ShowCoverPlaceholder = previewImages.Count == 0;
            OnPropertyChanged(nameof(ShowCoverImage));
        }

        /// <summary>
        /// Shows a single image filling the tile.
        /// </summary>
        public void SetCoverImage(string imagePath) =>
            SetCoverImages(string.IsNullOrWhiteSpace(imagePath) ? Array.Empty<string>() : new[] { imagePath });

        /// <summary>
        /// Shows a glyph when the folder holds nothing to preview.
        /// </summary>
        public void SetCoverPlaceholder(string glyph)
        {
            PlaceholderGlyph = glyph;
            SetCoverImages(Array.Empty<string>());
        }

        /// <summary>
        /// Cycles to the next preview image, the change fades in over the previous one.
        /// </summary>
        public void AdvancePreview()
        {
            if (previewImages.Count < 2)
                return;

            previewIndex = (previewIndex + 1) % previewImages.Count;
            // The transparent slot is loaded first, fading it in afterwards cross fades the two.
            if (PreviewOpacityA > PreviewOpacityB)
            {
                PreviewSlotB = previewImages[previewIndex];
                PreviewOpacityB = 1.0;
                PreviewOpacityA = 0.0;
            }
            else
            {
                PreviewSlotA = previewImages[previewIndex];
                PreviewOpacityA = 1.0;
                PreviewOpacityB = 0.0;
            }
        }

        public override string ToString() => Name;
    }
}

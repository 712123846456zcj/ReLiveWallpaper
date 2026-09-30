using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ImageMagick;
using Lively.Common;
using Lively.Common.Helpers.Files;
using Lively.Common.Helpers.Storage;
using Lively.Common.Services;
using Lively.Grpc.Client;
using Lively.Models;
using Lively.Models.Enums;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace Lively.UI.Shared.ViewModels
{
    /// <summary>
    /// Combines multiple pictures into a single picture wallpaper, laid out in a row.
    /// </summary>
    public partial class CollageWallpaperViewModel : ObservableObject
    {
        private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();
        private static readonly string[] ImageExtensions = [".jpg", ".jpeg", ".png", ".bmp", ".gif", ".webp", ".tif", ".tiff"];
        private const int FallbackWidth = 1920;
        private const int FallbackHeight = 1080;

        public event EventHandler OnRequestClose;

        private readonly IResourceService i18n;
        private readonly IFileService fileService;
        private readonly IUserSettingsClient userSettings;
        private readonly IDisplayManagerClient displayManager;
        private readonly IDesktopCoreClient desktopCore;
        private readonly LibraryViewModel libraryVm;

        public CollageWallpaperViewModel(IResourceService i18n,
            IFileService fileService,
            IUserSettingsClient userSettings,
            IDisplayManagerClient displayManager,
            IDesktopCoreClient desktopCore,
            LibraryViewModel libraryVm)
        {
            this.i18n = i18n;
            this.fileService = fileService;
            this.userSettings = userSettings;
            this.displayManager = displayManager;
            this.desktopCore = desktopCore;
            this.libraryVm = libraryVm;

            Images.CollectionChanged += (s, e) =>
            {
                OnPropertyChanged(nameof(HasImages));
                OnPropertyChanged(nameof(CanCreate));
                CreateCommand.NotifyCanExecuteChanged();
            };
        }

        /// <summary>
        /// Wallpaper created by the dialog, null when it was cancelled.
        /// </summary>
        public LibraryModel NewWallpaper { get; private set; }

        /// <summary>
        /// Pictures to combine, in the order they are laid out.
        /// </summary>
        public ObservableCollection<CollageImageViewModel> Images { get; } = new();

        public bool HasImages => Images.Count != 0;

        /// <summary>
        /// A collage needs at least two pictures to make sense.
        /// </summary>
        public bool CanCreate => Images.Count > 1 && !IsRunning;

        [ObservableProperty]
        private bool isRunning;

        [ObservableProperty]
        private string errorText;

        /// <summary>
        /// 0 horizontal, 1 vertical.
        /// </summary>
        [ObservableProperty]
        private int layoutIndex;

        /// <summary>
        /// 0 fill the cell and crop the overflow, 1 fit the picture inside the cell.
        /// </summary>
        [ObservableProperty]
        private int fitIndex;

        /// <summary>
        /// Spacing between the pictures, in screen pixels.
        /// </summary>
        [ObservableProperty]
        private double gap;

        partial void OnIsRunningChanged(bool value)
        {
            OnPropertyChanged(nameof(CanCreate));
            CreateCommand.NotifyCanExecuteChanged();
        }

        [RelayCommand]
        private async Task AddImages()
        {
            var files = await fileService.PickFileAsync([(i18n.GetString("TextPicture"), ImageExtensions)], true);
            foreach (var file in files)
            {
                if (!Images.Any(x => x.Path.Equals(file, StringComparison.OrdinalIgnoreCase)))
                    Images.Add(new CollageImageViewModel(file, RemoveImage, MoveImage));
            }
        }

        private void RemoveImage(CollageImageViewModel image) => Images.Remove(image);

        private void MoveImage(CollageImageViewModel image, int offset)
        {
            var index = Images.IndexOf(image);
            var target = index + offset;
            if (index < 0 || target < 0 || target >= Images.Count)
                return;

            Images.Move(index, target);
        }

        [RelayCommand(CanExecute = nameof(CanCreate))]
        private async Task Create()
        {
            ErrorText = null;
            IsRunning = true;

            var destDir = Path.Combine(userSettings.Settings.WallpaperDir, Constants.CommonPartialPaths.WallpaperInstallDir, Path.GetRandomFileName());
            try
            {
                var paths = Images.Select(x => x.Path).ToList();
                await Task.Run(() =>
                {
                    var mediaDir = Path.Combine(destDir, "media");
                    Directory.CreateDirectory(mediaDir);
                    var outputPath = Path.Combine(mediaDir, "collage.jpg");
                    CreateCollage(paths, outputPath);

                    using var thumbnail = new MagickImage(outputPath);
                    thumbnail.Thumbnail(new MagickGeometry()
                    {
                        Width = 480,
                        Height = 270,
                        IgnoreAspectRatio = false,
                        FillArea = true
                    });
                    thumbnail.Extent(480, 270, Gravity.Center);
                    thumbnail.Write(Path.Combine(destDir, "thumbnail.jpg"));
                });

                //Generate wallpaper metadata
                var infoModel = new LivelyInfoModel()
                {
                    AppVersion = desktopCore.AssemblyVersion.ToString(),
                    Title = i18n.GetString("TitleCollage"),
                    Desc = string.Format(i18n.GetString("CollageDesc/Text"), paths.Count),
                    Author = "Lively",
                    Type = WallpaperType.picture,
                    FileName = Path.Combine("media", "collage.jpg"),
                    Thumbnail = "thumbnail.jpg",
                    Preview = "thumbnail.jpg",
                    IsAbsolutePath = false,
                };
                JsonStorage<LivelyInfoModel>.StoreData(Path.Combine(destDir, "LivelyInfo.json"), infoModel);

                NewWallpaper = libraryVm.AddWallpaperFolder(destDir);
                OnRequestClose?.Invoke(this, EventArgs.Empty);
            }
            catch (Exception ex)
            {
                Logger.Error(ex);
                ErrorText = $"{i18n.GetString("TextError")}: {ex.Message}";
                await FileUtil.TryDeleteDirectoryAsync(destDir, 0, 1000);
            }
            finally
            {
                IsRunning = false;
            }
        }

        /// <summary>
        /// Lays the pictures out over a canvas matching the primary screen resolution.
        /// </summary>
        private void CreateCollage(IReadOnlyList<string> paths, string outputPath)
        {
            var (canvasWidth, canvasHeight) = GetCanvasSize();
            var spacing = (int)Math.Round(Gap);
            var count = paths.Count;
            // The last picture takes up the rounding remainder, there is no gap at the edges.
            var isVertical = LayoutIndex == 1;
            var cellLength = (isVertical ? canvasHeight - spacing * (count - 1) : canvasWidth - spacing * (count - 1)) / count;

            using var canvas = new MagickImage(MagickColors.Black, (uint)canvasWidth, (uint)canvasHeight);
            canvas.Quality = 92;
            for (int i = 0; i < count; i++)
            {
                var offset = i * (cellLength + spacing);
                var isLast = i == count - 1;
                if (isVertical)
                    DrawCell(canvas, paths[i], canvasWidth, isLast ? canvasHeight - offset : cellLength, 0, offset);
                else
                    DrawCell(canvas, paths[i], isLast ? canvasWidth - offset : cellLength, canvasHeight, offset, 0);
            }

            canvas.Write(outputPath, MagickFormat.Jpeg);
        }

        private void DrawCell(MagickImage canvas, string path, int cellWidth, int cellHeight, int x, int y)
        {
            using var image = new MagickImage(path);
            image.Strip();

            if (FitIndex == 0)
            {
                //Fill the cell, the overflow is cropped away.
                image.Resize(new MagickGeometry()
                {
                    Width = (uint)cellWidth,
                    Height = (uint)cellHeight,
                    FillArea = true
                });
                image.Crop((uint)cellWidth, (uint)cellHeight, Gravity.Center);
                canvas.Composite(image, x, y, CompositeOperator.Over);
                return;
            }

            //Fit the picture inside the cell, the leftover is painted with its average color.
            var background = new MagickColor(GetAverageColor(path) ?? "#000000");
            using var cell = new MagickImage(background, (uint)cellWidth, (uint)cellHeight);
            image.Resize(new MagickGeometry()
            {
                Width = (uint)cellWidth,
                Height = (uint)cellHeight
            });
            cell.Composite(image, Gravity.Center, CompositeOperator.Over);
            canvas.Composite(cell, x, y, CompositeOperator.Over);
        }

        private (int width, int height) GetCanvasSize()
        {
            var bounds = displayManager.PrimaryMonitor?.Bounds ?? Rectangle.Empty;
            if (bounds.Width <= 0 || bounds.Height <= 0)
                bounds = displayManager.DisplayMonitors.FirstOrDefault(x => x.Bounds.Width > 0)?.Bounds ?? Rectangle.Empty;

            return bounds.Width > 0 && bounds.Height > 0 ?
                (bounds.Width, bounds.Height) : (FallbackWidth, FallbackHeight);
        }

        /// <summary>
        /// Average color of the picture, used to fill the spare space of a cell.
        /// </summary>
        private static string GetAverageColor(string path)
        {
            try
            {
                using var image = new MagickImage(path);
                image.HasAlpha = false;
                //Downscaling to a single pixel is the average color of the picture.
                image.Resize(new MagickGeometry()
                {
                    Width = 1,
                    Height = 1,
                    IgnoreAspectRatio = true
                });
                // Read the pixel through the collection, the byte format respects the source bit depth.
                var rgb = image.GetPixels().ToByteArray(0, 0, 1, 1, "RGB");
                if (rgb.Length >= 3)
                    return $"#{rgb[0]:X2}{rgb[1]:X2}{rgb[2]:X2}";
            }
            catch (Exception e)
            {
                Logger.Error($"Failed to sample the average color: {e.Message}");
            }

            return null;
        }
    }
}

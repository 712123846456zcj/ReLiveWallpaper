using Lively.Common.Services;
using Lively.Grpc.Client;
using Lively.Models;
using Lively.Models.Enums;
using Lively.UI.Shared.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using System;
using System.IO;
using System.Linq;
using Windows.ApplicationModel.DataTransfer;
using Windows.Foundation;
using Lively.Common.Extensions;

namespace Lively.UI.WinUI.Views.Pages
{
    public sealed partial class LibraryView : Page
    {
        private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();
        /// <summary>
        /// Drives the folder tile previews, a single timer for the whole folder strip.
        /// </summary>
        private readonly DispatcherQueueTimer folderPreviewTimer;
        private LibraryModel selectedTile;
        private LibraryFolderItemViewModel selectedFolderTile;

        private readonly IResourceService i18n;
        private readonly IUserSettingsClient userSettings;
        private readonly IDesktopCoreClient desktopCore;
        private readonly LibraryViewModel libraryVm;
        private readonly IDialogService dialogService;
        private readonly IDisplayManagerClient displayManager;

        public LibraryView()
        {
            this.desktopCore = App.Services.GetRequiredService<IDesktopCoreClient>();
            this.libraryVm = App.Services.GetRequiredService<LibraryViewModel>();
            this.userSettings = App.Services.GetRequiredService<IUserSettingsClient>();
            this.dialogService = App.Services.GetRequiredService<IDialogService>();
            this.displayManager = App.Services.GetRequiredService<IDisplayManagerClient>();
            this.i18n = App.Services.GetRequiredService<IResourceService>();

            this.InitializeComponent();
            this.DataContext = libraryVm;

            folderPreviewTimer = DispatcherQueue.CreateTimer();
            folderPreviewTimer.Interval = TimeSpan.FromSeconds(2.5);
            folderPreviewTimer.IsRepeating = true;
            folderPreviewTimer.Tick += (s, e) => libraryVm.AdvanceFolderPreviews();
            // Only preview while the page is on screen.
            this.Loaded += (s, e) => folderPreviewTimer.Start();
            this.Unloaded += (s, e) => folderPreviewTimer.Stop();
        }

        #region library

        private async void contextMenu_Click(object sender, RoutedEventArgs e)
        {
            if (selectedTile == null)
                return;

            var s = sender as MenuFlyoutItem;
            var obj = selectedTile;
            switch (s.Name)
            {
                case "previewWallpaper":
                    await desktopCore.PreviewWallpaper(obj.LivelyInfoFolderPath);
                    break;
                case "showOnDisk":
                    await libraryVm.WallpaperShowOnDisk(obj);
                    break;
                case "setWallpaper":
                    DisplayMonitor monitor;
                    if (userSettings.Settings.RememberSelectedScreen)
                        monitor = userSettings.Settings.SelectedDisplay;
                    else
                        monitor = displayManager.DisplayMonitors.Count == 1 || userSettings.Settings.WallpaperArrangement != WallpaperArrangement.per ?
                           displayManager.DisplayMonitors.FirstOrDefault(x => x.IsPrimary) : await dialogService.ShowDisplayChooseDialogAsync();
                    if (monitor is null)
                        return;

                    await desktopCore.SetWallpaper(obj, monitor);
                    break;
                case "exportWallpaper":
                    await dialogService.ShowShareWallpaperDialogAsync(obj);
                    break;
                case "deleteWallpaper":
                    if (await dialogService.ShowDeleteWallpaperDialogAsync(obj))
                        await libraryVm.WallpaperDelete(obj);
                    break;
                case "customiseWallpaper":
                    await dialogService.ShowCustomiseWallpaperDialogAsync(obj);
                    break;
                case "editWallpaper":
                    // Show and confirm project structure for wallpapers that can be outside wallpaper directory and contain multiple files.
                    if (obj.LivelyInfo.IsAbsolutePath
                        && obj.LivelyInfo.Type.IsDirectoryProject()
                        && !await dialogService.ShowWallpaperProjectDirectoryDialogAsync(Path.GetDirectoryName(obj.FilePath)))
                        return;

                    var success = await desktopCore.EditWallpaper(obj.LivelyInfoFolderPath);
                    if (success)
                    {
                        libraryVm.RemoveWallpaper(obj);
                        libraryVm.AddWallpaperFolder(obj.LivelyInfoFolderPath);
                    }
                    break;
                case "moreInformation":
                    await dialogService.ShowAboutWallpaperDialogAsync(obj);
                    break;
                case "removeFromFolder":
                    libraryVm.SetWallpaperFolder(null, obj);
                    break;
                case "reportWallpaper":
                    await dialogService.ShowReportWallpaperDialogAsync(obj);
                    break;
            }
        }

        private void GridView_RightTapped(object sender, RightTappedRoutedEventArgs e)
        {
            try
            {
                var a = ((FrameworkElement)e.OriginalSource).DataContext;
                selectedTile = (LibraryModel)a;
                if (selectedTile.IsReadyToSet)
                {
                    BuildFolderMenu(selectedTile);
                    var item = sender as GridView;
                    contextMenu.ShowAt(item, e.GetPosition(item));
                    customiseWallpaper.IsEnabled = selectedTile.LivelyPropertyPath != null;
                }
            }
            catch
            {
                selectedTile = null;
                customiseWallpaper.IsEnabled = false;
            }
        }

        private void MoreButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var a = ((FrameworkElement)e.OriginalSource).DataContext;
                selectedTile = (LibraryModel)a;
                if (selectedTile.IsReadyToSet)
                {
                    customiseWallpaper.IsEnabled = selectedTile.LivelyPropertyPath != null;
                    BuildFolderMenu(selectedTile);
                    contextMenu.ShowAt((UIElement)e.OriginalSource, new Point(0, 0));
                }
            }
            catch
            {
                selectedTile = null;
                customiseWallpaper.IsEnabled = false;
            }
        }

        /// <summary>
        /// Fills the "add to folder" context menu with the folders available.
        /// </summary>
        private void BuildFolderMenu(LibraryModel wallpaper)
        {
            moveToFolder.Items.Clear();
            removeFromFolder.IsEnabled = false;
            if (wallpaper is null)
                return;

            var currentFolder = libraryVm.GetFolder(wallpaper);
            foreach (var folder in libraryVm.FolderItems.Where(x => !x.IsRoot))
            {
                var item = new MenuFlyoutItem()
                {
                    Text = folder.Name,
                    Tag = folder,
                    Icon = new FontIcon() { Glyph = folder == currentFolder ? "\uE73E" : "\uE8B7" },
                };
                item.Click += MoveToFolder_Click;
                moveToFolder.Items.Add(item);
            }

            if (moveToFolder.Items.Count != 0)
                moveToFolder.Items.Add(new MenuFlyoutSeparator());

            var newFolder = new MenuFlyoutItem()
            {
                Text = i18n.GetString("NewFolder/Text"),
                Icon = new FontIcon() { Glyph = "\uE8F4" },
            };
            newFolder.Click += NewFolderWithWallpaper_Click;
            moveToFolder.Items.Add(newFolder);

            removeFromFolder.IsEnabled = currentFolder is not null;
        }

        private void MoveToFolder_Click(object sender, RoutedEventArgs e)
        {
            if (selectedTile is null || (sender as MenuFlyoutItem)?.Tag is not LibraryFolderItemViewModel folder)
                return;

            libraryVm.SetWallpaperFolder(folder, selectedTile);
        }

        private async void NewFolderWithWallpaper_Click(object sender, RoutedEventArgs e)
        {
            if (selectedTile is null)
                return;

            var wallpaper = selectedTile;
            var result = await dialogService.ShowLibraryFolderDialogAsync(i18n.GetString("TitleCreateFolder"));
            if (result is null)
                return;

            var folder = libraryVm.CreateFolder(result.Value.name, result.Value.coverImage);
            libraryVm.SetWallpaperFolder(folder, wallpaper);
        }

        private void FolderListView_RightTapped(object sender, RightTappedRoutedEventArgs e)
        {
            try
            {
                selectedFolderTile = ((FrameworkElement)e.OriginalSource).DataContext as LibraryFolderItemViewModel;
                if (selectedFolderTile is null)
                    return;

                // The tile showing the entire library cannot be changed.
                openFolder.IsEnabled = !selectedFolderTile.IsRoot;
                renameFolder.IsEnabled = !selectedFolderTile.IsRoot;
                deleteFolder.IsEnabled = !selectedFolderTile.IsRoot;

                var item = sender as ListView;
                folderContextMenu.ShowAt(item, e.GetPosition(item));
            }
            catch
            {
                selectedFolderTile = null;
            }
        }

        private async void FolderMenu_Click(object sender, RoutedEventArgs e)
        {
            if (selectedFolderTile is null)
                return;

            var folder = selectedFolderTile;
            switch ((sender as MenuFlyoutItem).Name)
            {
                case "openFolder":
                    libraryVm.SelectedFolder = folder;
                    break;
                case "renameFolder":
                    {
                        var result = await dialogService.ShowLibraryFolderDialogAsync(i18n.GetString("TitleEditFolder"), folder.Name, folder.Data.CoverImage);
                        if (result is null)
                            return;

                        libraryVm.UpdateFolder(folder, result.Value.name, result.Value.coverImage);
                    }
                    break;
                case "deleteFolder":
                    {
                        var message = string.Format(i18n.GetString("DeleteFolderConfirm/Text"), folder.Name);
                        if (!await dialogService.ShowConfirmationDialogAsync(message))
                            return;

                        libraryVm.DeleteFolder(folder);
                    }
                    break;
            }
        }

        #endregion //library

        #region file drop

        private async void Page_Drop(object sender, DragEventArgs e)
        {
            this.AddFilePanel.Visibility = Visibility.Collapsed;

            if (e.DataView.Contains(StandardDataFormats.WebLink))
            {
                var uri = await e.DataView.GetWebLinkAsync();
                Logger.Info($"Dropped string {uri}");
                try
                {
                    await libraryVm.AddWallpaperLink(uri, true);
                }
                catch (Exception ie)
                {
                    await dialogService.ShowDialogAsync(ie.Message,
                        i18n.GetString("TextError"),
                        i18n.GetString("TextOk"));
                }
            }
            else if (e.DataView.Contains(StandardDataFormats.StorageItems))
            {
                var items = await e.DataView.GetStorageItemsAsync();
                if (items.Count == 1)
                {
                    var item = items[0].Path;
                    Logger.Info($"Dropped file {item}");
                    try
                    {
                        if (string.IsNullOrWhiteSpace(Path.GetExtension(item)))
                            return;
                    }
                    catch (ArgumentException)
                    {
                        Logger.Info($"Invalid character, skipping dropped file {item}");
                        return;
                    }

                    try
                    {
                        var creationType = await dialogService.ShowWallpaperCreateDialogAsync(item);
                        if (creationType is null)
                            return;

                        switch (creationType)
                        {
                            case WallpaperCreateType.none:
                                {
                                    await libraryVm.AddWallpaperFile(item, true);
                                }
                                break;
                            case WallpaperCreateType.depthmap:
                                {
                                    var result = await dialogService.ShowDepthWallpaperDialogAsync(item);
                                    if (result is not null)
                                        await desktopCore.SetWallpaper(result, userSettings.Settings.SelectedDisplay);
                                }
                                break;
                        }
                    }
                    catch (Exception ie)
                    {
                        await dialogService.ShowDialogAsync(ie.Message,
                            i18n.GetString("TextError"),
                            i18n.GetString("TextOk"));
                    }
                }
                else if (items.Count > 1)
                {
                    await App.Services.GetRequiredService<MainViewModel>().AddWallpapers(items.Select(x => x.Path).ToList());
                }
            }
        }

        private void Page_DragOver(object sender, DragEventArgs e)
        {
            e.AcceptedOperation = DataPackageOperation.Copy;
            if (e.DragUIOverride != null)
            {
                e.DragUIOverride.IsCaptionVisible = false;
                e.DragUIOverride.IsContentVisible = true;
            }
            this.AddFilePanel.Visibility = Visibility.Visible;
        }

        private void Page_DragLeave(object sender, DragEventArgs e)
        {
            this.AddFilePanel.Visibility = Visibility.Collapsed;
        }

        #endregion //file drop
    }
}
